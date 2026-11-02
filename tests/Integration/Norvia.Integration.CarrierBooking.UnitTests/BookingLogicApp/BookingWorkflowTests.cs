using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.BookingLogicApp;

/// <summary>
/// The P4 booking Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and P4. The
/// Logic Apps runtime runs only in Azure, so these tests read the definition; the map it calls is tested on its own, and
/// the local worker plays the same steps (ADR 0030).
/// </summary>
public sealed class BookingWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_booking')?['correlationId']}";

    private static readonly string[] TmsReports = [BookingWorkflow.ReportSent, BookingWorkflow.ReportNotDelivered];
    private static readonly string[] CalledActions = [BookingWorkflow.Send, BookingWorkflow.ReportSent, BookingWorkflow.ReportNotDelivered];

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheBookingSubscriptionOnTheShipmentEventsTopic()
    {
        var parameters = BookingWorkflow.Trigger["inputs"]!["parameters"]!;

        BookingWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ServiceProvider");
        parameters["topicName"]!.GetValue<string>().Should().Be("shipment-events");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("carrier-booking");
        BookingWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/serviceBus");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheReleasesCorrelationId() =>
        BookingWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['correlationId']}");

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        BookingWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        BookingWorkflow.Action(BookingWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        BookingWorkflow.Action(BookingWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        BookingWorkflow.Actions.Where(action => action.Key != BookingWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
        Statuses(BookingWorkflow.Action(BookingWorkflow.MapAction), BookingWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheReleaseTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = BookingWorkflow.Action(BookingWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "shipmentNumber", "carrierBookingNumber", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['contentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['carrierId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = BookingWorkflow.Action(BookingWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "shipmentNumber", "carrierBookingNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(BookingWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = BookingWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Should().Contain(operation => operation.Name == BookingWorkflow.Send);
        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == BookingWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain([BookingWorkflow.Tracking, BookingWorkflow.MapAction, BookingWorkflow.Send, BookingWorkflow.ReportSent, BookingWorkflow.ReportNotDelivered]);
    }

    [Fact]
    public void Map_RunsTheBookingMapOnTheMessageContent_AndTracksAnUnreadableReleaseAsFailed()
    {
        var map = BookingWorkflow.Action(BookingWorkflow.MapAction);

        map["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapShipmentReleased");
        map["inputs"]!["parameters"]!["shipmentReleased"]!.GetValue<string>().Should().Be("@{triggerBody()?['contentData']}");
        map["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("ShipmentReleasedUnreadable");
    }

    [Fact]
    public void Credential_IsReadFromKeyVaultByTheCarriersId_AndItsOutputIsSecure()
    {
        var credential = BookingWorkflow.Action(BookingWorkflow.Credential);

        credential["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/keyVault");
        credential["inputs"]!["parameters"]!["secretName"]!.GetValue<string>().Should().Be("carrier-endpoint-key-@{outputs('Track_the_booking')?['partyId']}");
        credential["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>()).Should().Equal("outputs");
    }

    [Fact]
    public void Send_PostsTheMappedBookingToTheCarriersEndpointInThePortal_WithTheReleasesCorrelationIdAndTheCredential()
    {
        var inputs = BookingWorkflow.Action(BookingWorkflow.Send)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('partnerPortalBaseUrl')}/carrier/v1/@{outputs('Track_the_booking')?['partyId']}/bookings");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["headers"]!["X-Api-Key"]!.GetValue<string>().Should().Be("@{body('Get_the_Carrier_endpoint_credential')?['value']}");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_release'))");
        Statuses(BookingWorkflow.Action(BookingWorkflow.Send), BookingWorkflow.Credential).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_NeverAppearsInTheWorkflow_OnlyAsTheOutputOfTheStepThatReadsIt()
    {
        var uses = BookingWorkflow.WorkflowText.Split(BookingWorkflow.Credential).Length - 1;

        // Its own name, the Send step's key header, and the runAfter of the Send step.
        uses.Should().Be(3);
        BookingWorkflow.Operations().Where(operation => operation.Operation.ToJsonString().Contains(BookingWorkflow.Credential, StringComparison.Ordinal))
            .Select(operation => operation.Name).Should().Equal(BookingWorkflow.Send);
    }

    [Fact]
    public void SecureData_IsOnlyOnTheStepsThatHandleTheCredential()
    {
        // Anywhere else it would hide the body the Integration operator needs (coding guidelines §7).
        var secure = BookingWorkflow.Operations().Where(operation => operation.Operation["runtimeConfiguration"]?["secureData"] is not null)
            .ToDictionary(operation => operation.Name, operation => operation.Operation["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Single()!.GetValue<string>());

        secure.Should().BeEquivalentTo(new Dictionary<string, string> { [BookingWorkflow.Credential] = "outputs", [BookingWorkflow.Send] = "inputs" });
    }

    [Theory]
    [InlineData(BookingWorkflow.Send)]
    [InlineData(BookingWorkflow.ReportSent)]
    [InlineData(BookingWorkflow.ReportNotDelivered)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AsTheIntakeDoes(string name)
    {
        // Logic Apps retries an exponential policy on a timeout, 429 and a server error, and never on another client error.
        var retry = BookingWorkflow.Action(name)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Theory]
    [InlineData(BookingWorkflow.Send)]
    [InlineData(BookingWorkflow.ReportSent)]
    [InlineData(BookingWorkflow.ReportNotDelivered)]
    public void Calls_CarryTheReleasesCorrelationId(string name) =>
        BookingWorkflow.Action(name)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);

    [Theory]
    [InlineData(BookingWorkflow.ReportSent, "report-sent")]
    [InlineData(BookingWorkflow.ReportNotDelivered, "report-not-delivered")]
    public void TmsReports_CallTheBookingsReportOperation_AsTheBookingIdentity(string name, string operation)
    {
        var inputs = BookingWorkflow.Action(name)["inputs"]!;

        inputs["uri"]!.GetValue<string>().Should().Be($"@{{parameters('tmsBaseUrl')}}/v1/carrier-bookings/@{{outputs('Track_the_booking')?['carrierBookingNumber']}}/{operation}");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('bookingIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
    }

    [Fact]
    public void Report_RunsWhateverTheCarrierAnswered_AndReportsSentOnASuccess_AndNotDeliveredOtherwise()
    {
        var report = BookingWorkflow.Action(BookingWorkflow.Report);

        Statuses(BookingWorkflow.Action("Read_the_answer"), BookingWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        Statuses(BookingWorkflow.Action("Track_the_answer"), "Read_the_answer").Should().BeEquivalentTo("Succeeded");
        Statuses(report, "Track_the_answer").Should().BeEquivalentTo("Succeeded");
        report["actions"]!.AsObject().Select(action => action.Key).Should().Equal(BookingWorkflow.ReportSent);
        report["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Contain(BookingWorkflow.ReportNotDelivered);
        var expression = report["expression"]!.ToJsonString();
        expression.Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
    }

    [Fact]
    public void ReportNotDelivered_CarriesTheStatusCodeAndAMessageOfAtMost500Characters()
    {
        var body = BookingWorkflow.Action(BookingWorkflow.ReportNotDelivered)["inputs"]!["body"]!.AsObject();

        body.Select(property => property.Key).Should().BeEquivalentTo("statusCode", "message");
        body["statusCode"]!.GetValue<string>().Should().Contain("outputs('Read_the_answer')?['statusCode']");
        body["message"]!.GetValue<string>().Should().Contain("min(500,");
    }

    [Fact]
    public void TmsReports_TrackTheOutcomeAndTheFailureCodeOfTheCall()
    {
        foreach (var name in TmsReports)
        {
            var tracked = BookingWorkflow.Action(name)["trackedProperties"]!.AsObject();

            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "correlationId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("TmsUnavailable").And.Contain("TmsCallRefused");
        }
    }

    [Fact]
    public void Send_TracksTheCarriersFailureAsUnavailableOrRefused()
    {
        var failureCode = BookingWorkflow.Action("Read_the_answer")["inputs"]!["failureCode"]!.GetValue<string>();

        failureCode.Should().Contain("CarrierUnavailable").And.Contain("CarrierRefused");
    }

    [Fact]
    public void Calls_OnlyTheOnesTheTrailNeedsAreTracked_BySendAndTheTwoReports() =>
        BookingWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(CalledActions);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        BookingWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = BookingWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "bookingIdentityId", "partnerPortalBaseUrl");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_UseTheBookingIdentity_NotAKey()
    {
        var connections = BookingWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("serviceBus", "keyVault");
        foreach (var (_, connection) in connections)
        {
            var authProvider = connection!["parameterValues"]!["authProvider"]!;
            authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('BOOKING_IDENTITY_ID')");
        }

        connections["serviceBus"]!["parameterValues"]!["fullyQualifiedNamespace"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_NAMESPACE')");
        connections["keyVault"]!["parameterValues"]!["VaultUri"]!.GetValue<string>().Should().Be("@appsetting('KEY_VAULT_URI')");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = BookingWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }
}
