using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierCancellationLogicApp;

/// <summary>
/// The P7 Carrier cancellation Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and P7. The
/// Logic Apps runtime runs only in Azure, so these tests read the definition; the map it calls is tested on its own, and
/// the local worker plays the same steps (ADR 0030).
/// </summary>
public sealed class CarrierCancellationWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_cancellation')?['correlationId']}";

    private static readonly string[] TmsReports = [CarrierCancellationWorkflow.ReportTold, CarrierCancellationWorkflow.ReportNotDelivered];
    private static readonly string[] CalledActions = [CarrierCancellationWorkflow.Send, CarrierCancellationWorkflow.ReportTold, CarrierCancellationWorkflow.ReportNotDelivered];

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheCarrierCancellationSubscriptionOnTheShipmentEventsTopic()
    {
        var path = CarrierCancellationWorkflow.Trigger["inputs"]!["path"]!.GetValue<string>();

        CarrierCancellationWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        path.Should().Contain("'shipment-events'");
        path.Should().Contain("'carrier-cancellation'");
        CarrierCancellationWorkflow.Trigger["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCancellationsCorrelationId() =>
        CarrierCancellationWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{coalesce(if(equals(length(string(coalesce(triggerBody(), ''))), 0), json('{}'), triggerBody())?['Properties']?['correlationId'], '00000000-0000-0000-0000-000000000000')}");

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        CarrierCancellationWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        CarrierCancellationWorkflow.Actions.Where(action => action.Key != CarrierCancellationWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
        Statuses(CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.MapAction), CarrierCancellationWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheCancellationTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Tracking)["inputs"]!.AsObject();

        // The Carrier booking number is a key of the contract and the reports' address.
        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "shipmentNumber", "carrierBookingNumber", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['carrierId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "shipmentNumber", "carrierBookingNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(CarrierCancellationWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = CarrierCancellationWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Should().Contain(operation => operation.Name == CarrierCancellationWorkflow.Send);
        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == CarrierCancellationWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain([CarrierCancellationWorkflow.Tracking, CarrierCancellationWorkflow.MapAction, CarrierCancellationWorkflow.Send, CarrierCancellationWorkflow.ReportTold, CarrierCancellationWorkflow.ReportNotDelivered]);
    }

    [Fact]
    public void Map_RunsTheCarrierCancellationMapOnTheMessageContent_AndTracksAnUnreadableCancellationAsFailed()
    {
        var map = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.MapAction);

        map["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapCarrierCancellation");
        map["inputs"]!["parameters"]!["carrierCancellationEvent"]!.GetValue<string>().Should().Be("@{triggerBody()?['ContentData']}");
        map["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("CarrierCancellationUnreadable");
    }

    [Fact]
    public void Trigger_LocksTheMessage_SoTheWorkflowSettlesIt() =>
        CarrierCancellationWorkflow.Trigger["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/head/peek");

    [Fact]
    public void UnreadableCancellation_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = CarrierCancellationWorkflow.Action("Dead_letter_the_unreadable_cancellation");
        var parameters = deadLetter["inputs"]!["queries"]!;

        Statuses(deadLetter, CarrierCancellationWorkflow.MapAction).Should().BeEquivalentTo("Failed");
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("CarrierCancellationUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("CarrierCancellationUnreadable");
        CarrierCancellationWorkflow.Action("Fail_the_run_on_the_unreadable_cancellation")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void MappedCancellation_IsCompletedBeforeAnythingIsSent_SoAFailedRunIsNotRedelivered()
    {
        Statuses(CarrierCancellationWorkflow.Action("Complete_the_message"), CarrierCancellationWorkflow.MapAction).Should().BeEquivalentTo("Succeeded");
        Statuses(CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Credential), "Complete_the_message").Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_IsReadFromKeyVaultByTheCarriersId_AndItsOutputIsSecure()
    {
        var credential = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Credential);

        credential["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/keyVault");
        credential["inputs"]!["parameters"]!["secretName"]!.GetValue<string>().Should().Be("carrier-endpoint-key-@{outputs('Track_the_cancellation')?['partyId']}");
        credential["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>()).Should().Equal("outputs");
    }

    [Fact]
    public void Send_PostsTheMappedCancellationToTheCarriersEndpointInThePortal_WithTheCancellationsCorrelationIdAndTheCredential()
    {
        var inputs = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Send)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('partnerPortalBaseUrl')}/carrier/v1/@{outputs('Track_the_cancellation')?['partyId']}/cancellations");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["headers"]!["X-Api-Key"]!.GetValue<string>().Should().Be("@{body('Get_the_Carrier_endpoint_credential')?['value']}");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_cancellation'))");
        Statuses(CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Send), CarrierCancellationWorkflow.Credential).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_NeverAppearsInTheWorkflow_OnlyAsTheOutputOfTheStepThatReadsIt()
    {
        var uses = CarrierCancellationWorkflow.WorkflowText.Split(CarrierCancellationWorkflow.Credential).Length - 1;

        // Its own name, the Send step's key header, and the runAfter of the Send step.
        uses.Should().Be(3);
        CarrierCancellationWorkflow.Operations().Where(operation => operation.Operation.ToJsonString().Contains(CarrierCancellationWorkflow.Credential, StringComparison.Ordinal))
            .Select(operation => operation.Name).Should().Equal(CarrierCancellationWorkflow.Send);
    }

    [Fact]
    public void SecureData_IsOnlyOnTheStepsThatHandleTheCredential()
    {
        // Anywhere else it would hide the body the Integration operator needs (coding guidelines §7).
        var secure = CarrierCancellationWorkflow.Operations().Where(operation => operation.Operation["runtimeConfiguration"]?["secureData"] is not null)
            .ToDictionary(operation => operation.Name, operation => operation.Operation["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Single()!.GetValue<string>());

        secure.Should().BeEquivalentTo(new Dictionary<string, string> { [CarrierCancellationWorkflow.Credential] = "outputs", [CarrierCancellationWorkflow.Send] = "inputs" });
    }

    [Theory]
    [InlineData(CarrierCancellationWorkflow.Send)]
    [InlineData(CarrierCancellationWorkflow.ReportTold)]
    [InlineData(CarrierCancellationWorkflow.ReportNotDelivered)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AsTheIntakeDoes(string name)
    {
        // Logic Apps retries an exponential policy on a timeout, 429 and a server error, and never on another client error.
        var retry = CarrierCancellationWorkflow.Action(name)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Theory]
    [InlineData(CarrierCancellationWorkflow.Send)]
    [InlineData(CarrierCancellationWorkflow.ReportTold)]
    [InlineData(CarrierCancellationWorkflow.ReportNotDelivered)]
    public void Calls_CarryTheCancellationsCorrelationId(string name) =>
        CarrierCancellationWorkflow.Action(name)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);

    [Theory]
    [InlineData(CarrierCancellationWorkflow.ReportTold, "report-cancellation-told")]
    [InlineData(CarrierCancellationWorkflow.ReportNotDelivered, "report-cancellation-not-delivered")]
    public void TmsReports_CallTheCancellationsReportOperation_AsTheCarrierCancellationIdentity(string name, string operation)
    {
        var inputs = CarrierCancellationWorkflow.Action(name)["inputs"]!;

        inputs["uri"]!.GetValue<string>().Should().Be($"@{{parameters('tmsBaseUrl')}}/v1/carrier-bookings/@{{outputs('Track_the_cancellation')?['carrierBookingNumber']}}/{operation}");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('carrierCancellationIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
    }

    [Fact]
    public void Report_RunsWhateverTheCarrierAnswered_AndReportsSentOnASuccess_AndNotDeliveredOtherwise()
    {
        var report = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.Report);

        Statuses(CarrierCancellationWorkflow.Action("Read_the_answer"), CarrierCancellationWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        Statuses(CarrierCancellationWorkflow.Action("Track_the_answer"), "Read_the_answer").Should().BeEquivalentTo("Succeeded");
        Statuses(report, "Track_the_answer").Should().BeEquivalentTo("Succeeded");
        report["actions"]!.AsObject().Select(action => action.Key).Should().Equal(CarrierCancellationWorkflow.ReportTold);
        report["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Contain(CarrierCancellationWorkflow.ReportNotDelivered);
        var expression = report["expression"]!.ToJsonString();
        expression.Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
    }

    [Fact]
    public void ReportNotDelivered_CarriesTheStatusCodeAndAMessageOfAtMost500Characters()
    {
        var body = CarrierCancellationWorkflow.Action(CarrierCancellationWorkflow.ReportNotDelivered)["inputs"]!["body"]!.AsObject();

        body.Select(property => property.Key).Should().BeEquivalentTo("statusCode", "message");
        body["statusCode"]!.GetValue<string>().Should().Contain("outputs('Read_the_answer')?['statusCode']");
        body["message"]!.GetValue<string>().Should().Contain("min(500,");
    }

    [Fact]
    public void TmsReports_TrackTheOutcomeAndTheFailureCodeOfTheCall()
    {
        foreach (var name in TmsReports)
        {
            var tracked = CarrierCancellationWorkflow.Action(name)["trackedProperties"]!.AsObject();

            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "correlationId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("TmsUnavailable").And.Contain("TmsCallRefused");
        }
    }

    [Fact]
    public void Send_TracksTheCarriersFailureAsUnavailableOrRefused()
    {
        var failureCode = CarrierCancellationWorkflow.Action("Read_the_answer")["inputs"]!["failureCode"]!.GetValue<string>();

        failureCode.Should().Contain("CarrierUnavailable").And.Contain("CarrierRefused");
    }

    [Fact]
    public void Calls_OnlyTheOnesTheTrailNeedsAreTracked_BySendAndTheTwoReports() =>
        CarrierCancellationWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(CalledActions);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        CarrierCancellationWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = CarrierCancellationWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "carrierCancellationIdentityId", "partnerPortalBaseUrl");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_UseTheCarrierCancellationIdentity_NotAKey()
    {
        var connections = CarrierCancellationWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("keyVault");
        foreach (var (_, connection) in connections)
        {
            var authProvider = connection!["parameterValues"]!["authProvider"]!;
            authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('CARRIER_CANCELLATION_IDENTITY_ID')");
        }
        connections["keyVault"]!["parameterValues"]!["VaultUri"]!.GetValue<string>().Should().Be("@appsetting('KEY_VAULT_URI')");
    }

    [Fact]
    public void EveryServiceBusStep_ReadsTheOneSubscriptionThroughTheManagedConnection()
    {
        var steps = CarrierCancellationWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection").ToList();

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>() == "servicebus"
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'shipment-events'", StringComparison.Ordinal)
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'carrier-cancellation'", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheIdentity_ForTheTokenStoreAndForServiceBus()
    {
        var connections = CarrierCancellationWorkflow.ConnectionsFile["managedApiConnections"]!.AsObject();
        var serviceBus = connections["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("servicebus");
        serviceBus["api"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_API_ID')");
        serviceBus["connection"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_ID')");
        serviceBus["connectionRuntimeUrl"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_RUNTIME_URL')");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('CARRIER_CANCELLATION_IDENTITY_ID')");
        }

        serviceBus["connectionProperties"]!["authentication"]!["audience"]!.GetValue<string>().Should().Be("https://servicebus.azure.net");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = CarrierCancellationWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }
}
