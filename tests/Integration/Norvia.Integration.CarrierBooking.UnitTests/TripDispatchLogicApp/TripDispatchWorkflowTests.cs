using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.TripDispatchLogicApp;

/// <summary>
/// The P11 Trip dispatch Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and P11. The
/// Logic Apps runtime runs only in Azure, so these tests read the definition; the local worker plays the same steps.
/// </summary>
public sealed class TripDispatchWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_trip')?['correlationId']}";

    private static readonly string[] CalledActions = [TripDispatchWorkflow.Send, TripDispatchWorkflow.ReportDelivered, TripDispatchWorkflow.ReportNotDelivered];

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    private const string SubscriptionPath = "/@{encodeURIComponent(encodeURIComponent('trip-events'))}/subscriptions/@{encodeURIComponent('trip-dispatch')}/messages/";

    [Fact]
    public void Trigger_IsTheTripDispatchSubscriptionOnTheTripEventsTopic_ThroughTheServiceBusManagedConnector()
    {
        var inputs = TripDispatchWorkflow.Trigger["inputs"]!;

        TripDispatchWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        inputs["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
        inputs["path"]!.GetValue<string>().Should().StartWith(SubscriptionPath);
    }

    [Fact]
    public void Trigger_PeeksTheMessageWithALock_SoTheWorkflowSettlesIt()
    {
        var inputs = TripDispatchWorkflow.Trigger["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("get");
        inputs["path"]!.GetValue<string>().Should().EndWith("messages/head/peek");
        inputs["queries"]!["subscriptionType"]!.GetValue<string>().Should().Be("Main");
        TripDispatchWorkflow.Trigger["recurrence"]!["frequency"]!.GetValue<string>().Should().Be("Minute");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheDispatchsCorrelationId_AndSurvivesAPollThatFindsNoMessage()
    {
        // A poll with nothing to peek answers 202 with an empty body, and the runtime evaluates the expression on it all the same.
        var clientTrackingId = TripDispatchWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>();

        clientTrackingId.Should().Contain("?['Properties']?['correlationId']").And.Contain("equals(length(string(coalesce(triggerBody(), ''))), 0)");
    }

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        TripDispatchWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        TripDispatchWorkflow.Action(TripDispatchWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        TripDispatchWorkflow.Action(TripDispatchWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        TripDispatchWorkflow.Actions.Where(action => action.Key != TripDispatchWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheEventTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = TripDispatchWorkflow.Action(TripDispatchWorkflow.Tracking)["inputs"]!.AsObject();

        // The version is not a key of the contract; it is the reports' address.
        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "tripNumber", "version", "eventType", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['driver']?['driverId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = TripDispatchWorkflow.Action(TripDispatchWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "tripNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(TripDispatchWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = TripDispatchWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == TripDispatchWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain(
            [TripDispatchWorkflow.Tracking, TripDispatchWorkflow.DeadLetter, TripDispatchWorkflow.Send, TripDispatchWorkflow.ReportDelivered, TripDispatchWorkflow.ReportNotDelivered]);
    }

    [Fact]
    public void Check_RunsWhetherOrNotTheTripCouldBeRead_AndLooksOnlyAtTheTrackedOutputsSoItCantFail()
    {
        var check = TripDispatchWorkflow.Action(TripDispatchWorkflow.Check);

        Statuses(check, TripDispatchWorkflow.Tracking).Should().BeEquivalentTo("Succeeded", "Failed");
        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("TripDispatched").And.Contain("correlationId").And.Contain("tripNumber").And.Contain("partyId").And.Contain("version");
        expression.Should().NotContain("triggerBody", "a message that isn't JSON would fail the check instead of being dead-lettered");
    }

    [Fact]
    public void UnreadableTrip_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = TripDispatchWorkflow.Action(TripDispatchWorkflow.DeadLetter);
        var inputs = deadLetter["inputs"]!;
        var queries = inputs["queries"]!;

        TripDispatchWorkflow.Action(TripDispatchWorkflow.Check)["else"]!["actions"]!.AsObject().Select(action => action.Key)
            .Should().Equal(TripDispatchWorkflow.DeadLetter, TripDispatchWorkflow.FailTheRun);
        deadLetter["type"]!.GetValue<string>().Should().Be("ApiConnection");
        inputs["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
        inputs["method"]!.GetValue<string>().Should().Be("post");
        inputs["path"]!.GetValue<string>().Should().Be(SubscriptionPath + "deadletter");
        queries["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        queries["deadLetterReason"]!.GetValue<string>().Should().Be("TripDispatchUnreadable");
        queries["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("TripDispatchUnreadable");
        TripDispatchWorkflow.Action(TripDispatchWorkflow.FailTheRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void ReadableTrip_IsCompletedBeforeAnythingIsSent_SoAFailedRunIsNotRedelivered()
    {
        var complete = TripDispatchWorkflow.Action(TripDispatchWorkflow.Complete);

        complete["runAfter"]!.AsObject().Should().BeEmpty();
        complete["type"]!.GetValue<string>().Should().Be("ApiConnection");
        complete["inputs"]!["method"]!.GetValue<string>().Should().Be("delete");
        complete["inputs"]!["path"]!.GetValue<string>().Should().Be(SubscriptionPath + "complete");
        complete["inputs"]!["queries"]!["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        TripDispatchWorkflow.Action(TripDispatchWorkflow.Check)["actions"]!.AsObject().Select(action => action.Key).Should().StartWith(TripDispatchWorkflow.Complete);
        Statuses(TripDispatchWorkflow.Action(TripDispatchWorkflow.Credential), TripDispatchWorkflow.Complete).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_IsReadFromKeyVault_AndItsOutputIsSecure()
    {
        var credential = TripDispatchWorkflow.Action(TripDispatchWorkflow.Credential);

        credential["inputs"]!["serviceProviderConfiguration"]!["serviceProviderId"]!.GetValue<string>().Should().Be("/serviceProviders/keyVault");
        credential["inputs"]!["parameters"]!["secretName"]!.GetValue<string>().Should().Be("driver-app-endpoint-key");
        credential["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>()).Should().Equal("outputs");
    }

    [Fact]
    public void Send_PostsTheTripAsTheEventCarriesIt_ToTheDriverAppsTripEndpoint_WithTheDispatchsCorrelationIdAndTheCredential()
    {
        var send = TripDispatchWorkflow.Action(TripDispatchWorkflow.Send);
        var inputs = send["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('driverAppBaseUrl')}/v1/trips");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["headers"]!["X-Api-Key"]!.GetValue<string>().Should().Be("@{body('Get_the_Driver_app_endpoint_credential')?['value']}");
        inputs["body"]!.GetValue<string>().Should().Be("@json(base64ToString(triggerBody()?['ContentData']))");
        Statuses(send, TripDispatchWorkflow.Credential).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Credential_NeverAppearsInTheWorkflow_OnlyAsTheOutputOfTheStepThatReadsIt()
    {
        TripDispatchWorkflow.Operations().Where(operation => operation.Operation.ToJsonString().Contains(TripDispatchWorkflow.Credential, StringComparison.Ordinal))
            .Select(operation => operation.Name).Should().Equal(TripDispatchWorkflow.Check, TripDispatchWorkflow.Send);
    }

    [Fact]
    public void SecureData_IsOnlyOnTheStepsThatHandleTheCredential()
    {
        // Anywhere else it would hide the body the Integration operator needs (coding guidelines §7).
        var secure = TripDispatchWorkflow.Operations().Where(operation => operation.Operation["runtimeConfiguration"]?["secureData"] is not null)
            .ToDictionary(operation => operation.Name, operation => operation.Operation["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Single()!.GetValue<string>());

        secure.Should().BeEquivalentTo(new Dictionary<string, string> { [TripDispatchWorkflow.Credential] = "outputs", [TripDispatchWorkflow.Send] = "inputs" });
    }

    [Theory]
    [InlineData(TripDispatchWorkflow.Send)]
    [InlineData(TripDispatchWorkflow.ReportDelivered)]
    [InlineData(TripDispatchWorkflow.ReportNotDelivered)]
    public void Calls_RetryWithTheBoundedExponentialPolicy_AsTheOtherLogicAppsDo(string name)
    {
        // Logic Apps retries an exponential policy on a timeout, 429 and a server error, and never on another client error.
        var retry = TripDispatchWorkflow.Action(name)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Theory]
    [InlineData(TripDispatchWorkflow.Send)]
    [InlineData(TripDispatchWorkflow.ReportDelivered)]
    [InlineData(TripDispatchWorkflow.ReportNotDelivered)]
    public void Calls_CarryTheDispatchsCorrelationId(string name) =>
        TripDispatchWorkflow.Action(name)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);

    [Theory]
    [InlineData(TripDispatchWorkflow.ReportDelivered, "report-delivered")]
    [InlineData(TripDispatchWorkflow.ReportNotDelivered, "report-not-delivered")]
    public void TmsReports_CallTheVersionsReportOperation_AsTheTripDispatchIdentity(string name, string operation)
    {
        var inputs = TripDispatchWorkflow.Action(name)["inputs"]!;

        inputs["uri"]!.GetValue<string>().Should().Be(
            $"@{{parameters('tmsBaseUrl')}}/v1/trips/@{{outputs('Track_the_trip')?['tripNumber']}}/versions/@{{outputs('Track_the_trip')?['version']}}/{operation}");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('tripDispatchIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
    }

    [Fact]
    public void Report_RunsWhateverTheDriverAppAnswered_AndReportsDeliveredOnASuccess_AndNotDeliveredOtherwise()
    {
        var report = TripDispatchWorkflow.Action(TripDispatchWorkflow.Report);

        Statuses(TripDispatchWorkflow.Action("Read_the_answer"), TripDispatchWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        Statuses(TripDispatchWorkflow.Action("Track_the_answer"), "Read_the_answer").Should().BeEquivalentTo("Succeeded");
        Statuses(report, "Track_the_answer").Should().BeEquivalentTo("Succeeded");
        report["actions"]!.AsObject().Select(action => action.Key).Should().Equal(TripDispatchWorkflow.ReportDelivered);
        report["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Contain(TripDispatchWorkflow.ReportNotDelivered);
        var expression = report["expression"]!.ToJsonString();
        expression.Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
    }

    [Fact]
    public void ReportNotDelivered_CarriesTheTechnicalCause_WithTheStatusTheAnswerOfAtMost500CharactersAndTheAttempts()
    {
        var body = TripDispatchWorkflow.Action(TripDispatchWorkflow.ReportNotDelivered)["inputs"]!["body"]!.AsObject();

        body.Select(property => property.Key).Should().Equal("cause");
        var cause = body["cause"]!.GetValue<string>();
        cause.Should().Contain("outputs('Read_the_answer')?['statusCode']");
        cause.Should().Contain("min(500,");
        cause.Should().Contain("retryHistory");
    }

    [Fact]
    public void TmsReports_TrackTheOutcomeAndTheFailureCodeOfTheCall()
    {
        foreach (var name in new[] { TripDispatchWorkflow.ReportDelivered, TripDispatchWorkflow.ReportNotDelivered })
        {
            var tracked = TripDispatchWorkflow.Action(name)["trackedProperties"]!.AsObject();

            tracked.Select(property => property.Key).Should().Contain(["outcome", "failureCode", "correlationId"]);
            tracked["failureCode"]!.GetValue<string>().Should().Contain("TmsUnavailable").And.Contain("TmsCallRefused");
        }
    }

    [Fact]
    public void Send_TracksTheDriverAppsFailureAsUnavailableOrRefused()
    {
        var failureCode = TripDispatchWorkflow.Action("Read_the_answer")["inputs"]!["failureCode"]!.GetValue<string>();

        failureCode.Should().Contain("DriverAppUnavailable").And.Contain("DriverAppRefused");
    }

    [Fact]
    public void Calls_AreTheDriverAppAndTheTwoReports() =>
        TripDispatchWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().BeEquivalentTo(CalledActions);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        TripDispatchWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = TripDispatchWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "tripDispatchIdentityId", "driverAppBaseUrl");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_UseTheTripDispatchIdentity_NotAKey()
    {
        var connections = TripDispatchWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("keyVault");
        var authProvider = connections["keyVault"]!["parameterValues"]!["authProvider"]!;
        authProvider["Type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        authProvider["Identity"]!.GetValue<string>().Should().Be("@appsetting('TRIP_DISPATCH_IDENTITY_ID')");
        connections["keyVault"]!["parameterValues"]!["VaultUri"]!.GetValue<string>().Should().Be("@appsetting('KEY_VAULT_URI')");
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheTripDispatchIdentity_ForTheTokenStoreAndForServiceBus()
    {
        var connections = TripDispatchWorkflow.ConnectionsFile["managedApiConnections"]!.AsObject();
        var serviceBus = connections["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("servicebus");
        serviceBus["api"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_API_ID')");
        serviceBus["connection"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_ID')");
        serviceBus["connectionRuntimeUrl"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_RUNTIME_URL')");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('TRIP_DISPATCH_IDENTITY_ID')");
        }

        serviceBus["connectionProperties"]!["authentication"]!["audience"]!.GetValue<string>().Should().Be("https://servicebus.azure.net");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = TripDispatchWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }
}
