using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ShipperNotificationLogicApp;

/// <summary>
/// The P7 Shipper notification Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and P7. The
/// Logic Apps runtime runs only in Azure, so these tests read the definition; the map it calls is tested on its own, and
/// the local worker plays the same steps (ADR 0030).
/// </summary>
public sealed class ShipperNotificationWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_notification')?['correlationId']}";

    private static readonly string[] TmsReports = [ShipperNotificationWorkflow.ReportSent, ShipperNotificationWorkflow.ReportNotDelivered];
    private static readonly string[] CalledActions = [ShipperNotificationWorkflow.Send, ShipperNotificationWorkflow.ReportSent, ShipperNotificationWorkflow.ReportNotDelivered];

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheShipperNotificationSubscriptionOnTheShipmentEventsTopic()
    {
        var parameters = ShipperNotificationWorkflow.Trigger["inputs"]!["parameters"]!;

        ShipperNotificationWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ServiceProvider");
        parameters["topicName"]!.GetValue<string>().Should().Be("shipment-events");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("shipper-notification");
        ShipperNotificationWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/serviceBus");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheNotificationsCorrelationId() =>
        ShipperNotificationWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['userProperties']?['correlationId']}");

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        ShipperNotificationWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        ShipperNotificationWorkflow.Actions.Where(action => action.Key != ShipperNotificationWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
        Statuses(ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.MapAction), ShipperNotificationWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheNotificationTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Tracking)["inputs"]!.AsObject();

        // The notification ID is no key of the contract: the report's address needs it, so it is taken but never tracked.
        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "shipmentNumber", "transportOrderNumber", "notificationId", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['contentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['shipperId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "shipmentNumber", "transportOrderNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(ShipperNotificationWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = ShipperNotificationWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Should().Contain(operation => operation.Name == ShipperNotificationWorkflow.Send);
        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == ShipperNotificationWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain([ShipperNotificationWorkflow.Tracking, ShipperNotificationWorkflow.MapAction, ShipperNotificationWorkflow.Send, ShipperNotificationWorkflow.ReportSent, ShipperNotificationWorkflow.ReportNotDelivered]);
    }

    [Fact]
    public void Map_RunsTheShipperNotificationMapOnTheMessageContent_AndTracksAnUnreadableNotificationAsFailed()
    {
        var map = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.MapAction);

        map["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapShipperNotificationRequested");
        map["inputs"]!["parameters"]!["shipperNotificationRequested"]!.GetValue<string>().Should().Be("@{triggerBody()?['contentData']}");
        map["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("ShipperNotificationRequestedUnreadable");
    }

    [Fact]
    public void Trigger_LocksTheMessage_SoTheWorkflowSettlesIt() =>
        ShipperNotificationWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("peekLockTopicMessages");

    [Fact]
    public void UnreadableNotification_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = ShipperNotificationWorkflow.Action("Dead_letter_the_unreadable_notification");
        var parameters = deadLetter["inputs"]!["parameters"]!;

        Statuses(deadLetter, ShipperNotificationWorkflow.MapAction).Should().BeEquivalentTo("Failed");
        deadLetter["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("deadLetterTopicMessage");
        parameters["subscriptionName"]!.GetValue<string>().Should().Be("shipper-notification");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['lockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("ShipperNotificationRequestedUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("ShipperNotificationRequestedUnreadable");
        ShipperNotificationWorkflow.Action("Fail_the_run_on_the_unreadable_notification")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void MappedNotification_IsCompletedBeforeAnythingIsSent_SoAFailedRunIsNotRedelivered()
    {
        Statuses(ShipperNotificationWorkflow.Action("Complete_the_message"), ShipperNotificationWorkflow.MapAction).Should().BeEquivalentTo("Succeeded");
        Statuses(ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Credential), "Complete_the_message").Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_IsReadFromKeyVaultByTheShippersId_AndItsOutputIsSecure()
    {
        var credential = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Credential);

        credential["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/keyVault");
        credential["inputs"]!["parameters"]!["secretName"]!.GetValue<string>().Should().Be("shipper-endpoint-key-@{outputs('Track_the_notification')?['partyId']}");
        credential["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>()).Should().Equal("outputs");
    }

    [Fact]
    public void Send_PostsTheMappedNotificationToTheShippersEndpointInThePortal_WithTheNotificationsCorrelationIdAndTheCredential()
    {
        var inputs = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Send)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('partnerPortalBaseUrl')}/shipper/v1/@{outputs('Track_the_notification')?['partyId']}/notifications");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["headers"]!["X-Api-Key"]!.GetValue<string>().Should().Be("@{body('Get_the_Shipper_endpoint_credential')?['value']}");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_notification'))");
        Statuses(ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Send), ShipperNotificationWorkflow.Credential).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_NeverAppearsInTheWorkflow_OnlyAsTheOutputOfTheStepThatReadsIt()
    {
        var uses = ShipperNotificationWorkflow.WorkflowText.Split(ShipperNotificationWorkflow.Credential).Length - 1;

        // Its own name, the Send step's key header, and the runAfter of the Send step.
        uses.Should().Be(3);
        ShipperNotificationWorkflow.Operations().Where(operation => operation.Operation.ToJsonString().Contains(ShipperNotificationWorkflow.Credential, StringComparison.Ordinal))
            .Select(operation => operation.Name).Should().Equal(ShipperNotificationWorkflow.Send);
    }

    [Fact]
    public void SecureData_IsOnlyOnTheStepsThatHandleTheCredential()
    {
        // Anywhere else it would hide the body the Integration operator needs (coding guidelines §7).
        var secure = ShipperNotificationWorkflow.Operations().Where(operation => operation.Operation["runtimeConfiguration"]?["secureData"] is not null)
            .ToDictionary(operation => operation.Name, operation => operation.Operation["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Single()!.GetValue<string>());

        secure.Should().BeEquivalentTo(new Dictionary<string, string> { [ShipperNotificationWorkflow.Credential] = "outputs", [ShipperNotificationWorkflow.Send] = "inputs" });
    }

    [Theory]
    [InlineData(ShipperNotificationWorkflow.Send)]
    [InlineData(ShipperNotificationWorkflow.ReportSent)]
    [InlineData(ShipperNotificationWorkflow.ReportNotDelivered)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AsTheIntakeDoes(string name)
    {
        // Logic Apps retries an exponential policy on a timeout, 429 and a server error, and never on another client error.
        var retry = ShipperNotificationWorkflow.Action(name)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Theory]
    [InlineData(ShipperNotificationWorkflow.Send)]
    [InlineData(ShipperNotificationWorkflow.ReportSent)]
    [InlineData(ShipperNotificationWorkflow.ReportNotDelivered)]
    public void Calls_CarryTheNotificationsCorrelationId(string name) =>
        ShipperNotificationWorkflow.Action(name)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);

    [Theory]
    [InlineData(ShipperNotificationWorkflow.ReportSent, "report-sent")]
    [InlineData(ShipperNotificationWorkflow.ReportNotDelivered, "report-not-delivered")]
    public void TmsReports_CallTheNotificationsReportOperation_AsTheShipperNotificationIdentity(string name, string operation)
    {
        var inputs = ShipperNotificationWorkflow.Action(name)["inputs"]!;

        inputs["uri"]!.GetValue<string>().Should().Be($"@{{parameters('tmsBaseUrl')}}/v1/shipper-notifications/@{{outputs('Track_the_notification')?['notificationId']}}/{operation}");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('shipperNotificationIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
    }

    [Fact]
    public void Report_RunsWhateverTheShipperAnswered_AndReportsSentOnASuccess_AndNotDeliveredOtherwise()
    {
        var report = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Report);

        Statuses(report, ShipperNotificationWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        report["actions"]!.AsObject().Select(action => action.Key).Should().Equal(ShipperNotificationWorkflow.ReportSent);
        report["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Contain(ShipperNotificationWorkflow.ReportNotDelivered);
        var expression = report["expression"]!.ToJsonString();
        expression.Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
    }

    [Fact]
    public void ReportNotDelivered_CarriesTheStatusCodeAndAMessageOfAtMost500Characters()
    {
        var body = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.ReportNotDelivered)["inputs"]!["body"]!.AsObject();

        body.Select(property => property.Key).Should().BeEquivalentTo("statusCode", "message");
        body["statusCode"]!.GetValue<string>().Should().Contain("coalesce(actions('Send_to_the_Shipper')?['outputs']?['statusCode'], 0)");
        body["message"]!.GetValue<string>().Should().Contain("min(500,");
    }

    [Fact]
    public void TmsReports_TrackTheOutcomeAndTheFailureCodeOfTheCall()
    {
        foreach (var name in TmsReports)
        {
            var tracked = ShipperNotificationWorkflow.Action(name)["trackedProperties"]!.AsObject();

            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "transportOrderNumber", "correlationId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("TmsUnavailable").And.Contain("TmsCallRefused");
        }
    }

    [Fact]
    public void Send_TracksTheShippersFailureAsUnavailableOrRefused()
    {
        var failureCode = ShipperNotificationWorkflow.Action(ShipperNotificationWorkflow.Send)["trackedProperties"]!["failureCode"]!.GetValue<string>();

        failureCode.Should().Contain("ShipperUnavailable").And.Contain("ShipperRefused");
    }

    [Fact]
    public void Calls_OnlyTheOnesTheTrailNeedsAreTracked_BySendAndTheTwoReports() =>
        ShipperNotificationWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(CalledActions);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        ShipperNotificationWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = ShipperNotificationWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "shipperNotificationIdentityId", "partnerPortalBaseUrl");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_UseTheShipperNotificationIdentity_NotAKey()
    {
        var connections = ShipperNotificationWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("serviceBus", "keyVault");
        foreach (var (_, connection) in connections)
        {
            var authProvider = connection!["parameterValues"]!["authProvider"]!;
            authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('SHIPPER_NOTIFICATION_IDENTITY_ID')");
        }

        connections["serviceBus"]!["parameterValues"]!["fullyQualifiedNamespace"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_NAMESPACE')");
        connections["keyVault"]!["parameterValues"]!["VaultUri"]!.GetValue<string>().Should().Be("@appsetting('KEY_VAULT_URI')");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = ShipperNotificationWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }
}
