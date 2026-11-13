using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.MasterDataSyncLogicApp;

/// <summary>
/// The E3 master-data sync Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and E3. The Logic Apps runtime
/// runs only in Azure, so these tests read the definition. The workflow maps the ERP's event to the TMS command.
/// </summary>
public sealed class MasterDataSyncWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_master_data_record')?['correlationId']}";

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    private static JsonObject Parameters(string action) => MasterDataSyncWorkflow.Action(action)["inputs"]!["queries"]!.AsObject();

    [Fact]
    public void Trigger_IsTheMasterDataSyncSubscriptionOnTheMasterDataEventsTopic_AndLocksTheMessage()
    {
        var inputs = MasterDataSyncWorkflow.Trigger["inputs"]!;

        MasterDataSyncWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        inputs["path"]!.GetValue<string>().Should().Contain("'master-data-events'");
        inputs["path"]!.GetValue<string>().Should().Contain("'master-data-sync'");
        MasterDataSyncWorkflow.Trigger["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
        inputs["path"]!.GetValue<string>().Should().EndWith("/messages/head/peek");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCorrelationId() =>
        MasterDataSyncWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{coalesce(if(equals(length(string(coalesce(triggerBody(), ''))), 0), json('{}'), triggerBody())?['Properties']?['correlationId'], '00000000-0000-0000-0000-000000000000')}");

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        MasterDataSyncWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        MasterDataSyncWorkflow.Actions.Where(action => action.Key != MasterDataSyncWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheEventTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "eventType", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['shipper']?['id']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(MasterDataSyncWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStepsAfterTracking_TrackTheFlowByItsStableName_AndTheKeysTheyKnow()
    {
        var tracked = new[] { MasterDataSyncWorkflow.Replace, MasterDataSyncWorkflow.DeadLetterRefused, MasterDataSyncWorkflow.DeadLetterUnreadable }
            .Select(name => MasterDataSyncWorkflow.Action(name)["trackedProperties"]!.AsObject()).ToList();

        tracked.Should().OnlyContain(properties => properties["flow"]!.GetValue<string>() == MasterDataSyncWorkflow.Flow);
        tracked.Should().OnlyContain(properties => properties.Count >= 5);
        foreach (var properties in tracked)
        {
            properties.Select(property => property.Key).Should().Contain(["correlationId", "partyId", "sourceMessageId", "outcome", "failureCode"]);
        }
    }

    [Fact]
    public void Check_RunsWhetherOrNotTheEventCouldBeRead_AndLooksOnlyAtTheTrackedOutputsSoItCantFail()
    {
        var check = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Check);

        Statuses(check, MasterDataSyncWorkflow.Tracking).Should().BeEquivalentTo("Succeeded", "Failed");
        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("Shipper").And.Contain("correlationId").And.Contain("partyId");
        expression.Should().NotContain("triggerBody", "a message that isn't JSON would fail the check instead of being dead-lettered");
    }

    [Fact]
    public void UnreadableMessage_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.DeadLetterUnreadable);
        var parameters = Parameters(MasterDataSyncWorkflow.DeadLetterUnreadable);

        MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Check)["else"]!["actions"]!.AsObject().Select(action => action.Key)
            .Should().Equal(MasterDataSyncWorkflow.DeadLetterUnreadable, MasterDataSyncWorkflow.FailTheUnreadableRun);
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("MasterDataUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent to the TMS");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("MasterDataUnreadable");
        MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.FailTheUnreadableRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void Replace_PutsTheWholeShipper_ToTheTmsApisShipperEndpoint_WithTheCorrelationId()
    {
        var replace = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Replace);
        var inputs = replace["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("PUT");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('tmsBaseUrl')}/v1/shippers/@{encodeUriComponent(outputs('Track_the_master_data_record')?['partyId'])}");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["body"]!.AsObject().Select(field => field.Key).Should().BeEquivalentTo("name", "active", "revision");
        replace["runAfter"]!.AsObject().Should().BeEmpty("it is the first step of the check's branch for a readable event");
        MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Check)["actions"]!.AsObject().Select(action => action.Key).Should().Equal(MasterDataSyncWorkflow.Replace, MasterDataSyncWorkflow.Settle);
    }

    [Fact]
    public void Replace_IsTheOnlyCall_AndAuthenticatesAsTheMasterDataSyncIdentityForTheTmsApisAudience()
    {
        var authentication = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Replace)["inputs"]!["authentication"]!;

        authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        authentication["identity"]!.GetValue<string>().Should().Be("@parameters('masterDataSyncIdentityId')");
        authentication["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
        MasterDataSyncWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().Equal(MasterDataSyncWorkflow.Replace);
    }

    [Fact]
    public void Replace_RetriesWithTheBoundedExponentialPolicy_AsTheOtherLogicAppsDo()
    {
        // Logic Apps retries an exponential policy on a timeout, 408, 429 and a server error, and never on another client error, a conflict included.
        var retry = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Replace)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Fact]
    public void Replace_TracksTheOutcomeAndTheFailureCode_UnavailableForAnUnansweredCallElseRefused()
    {
        var tracked = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Replace)["trackedProperties"]!;

        tracked["outcome"]!.GetValue<string>().Should().Contain("'Succeeded'").And.Contain("'Failed'");
        var failureCode = tracked["failureCode"]!.GetValue<string>();
        failureCode.Should().Contain("'TmsUnavailable'").And.Contain("'TmsCallRefused'");
        failureCode.Should().Contain("408").And.Contain("429").And.Contain("500");
    }

    [Fact]
    public void Settle_RunsWhateverTheTmsAnswered_AndCompletesTheMessageOnlyOnASuccess()
    {
        var settle = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Settle);

        Statuses(settle, MasterDataSyncWorkflow.Replace).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        settle["expression"]!.ToJsonString().Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
        settle["actions"]!.AsObject().Select(action => action.Key).Should().Equal(MasterDataSyncWorkflow.Complete);
        var complete = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Complete);
        complete["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/complete");
        Parameters(MasterDataSyncWorkflow.Complete)["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
    }

    [Fact]
    public void Settle_DeadLettersAClientErrorAConflictOrRetriesUsedUp_WithTmsCallRefusedOrTmsUnavailableAsReasonAndTheMessageAsDescription_AndFailsTheRun()
    {
        var settle = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.Settle);
        var deadLetter = MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.DeadLetterRefused);
        var parameters = Parameters(MasterDataSyncWorkflow.DeadLetterRefused);

        settle["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Equal(MasterDataSyncWorkflow.DeadLetterRefused, MasterDataSyncWorkflow.FailTheRefusedRun);
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Contain("'TmsUnavailable'").And.Contain("'TmsCallRefused'");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("['body']?['detail']").And.Contain("['body']?['title']");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("@action()?['inputs']?['parameters']?['deadLetterReason']");
        MasterDataSyncWorkflow.Action(MasterDataSyncWorkflow.FailTheRefusedRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void EveryServiceBusStep_SettlesTheMessageByItsLockToken()
    {
        var steps = MasterDataSyncWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection" && operation.Name != "trigger").ToList();

        steps.Select(step => step.Name).Should().BeEquivalentTo(MasterDataSyncWorkflow.Complete, MasterDataSyncWorkflow.DeadLetterRefused, MasterDataSyncWorkflow.DeadLetterUnreadable);
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["queries"]!["lockToken"]!.GetValue<string>() == "@triggerBody()?['LockToken']");
    }

    [Fact]
    public void NoStepHidesItsBodies_BecauseNothingInTheWorkflowIsASecret() =>
        MasterDataSyncWorkflow.Operations().Should().OnlyContain(operation => operation.Operation["runtimeConfiguration"] == null || operation.Operation["runtimeConfiguration"]!["secureData"] == null);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        MasterDataSyncWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = MasterDataSyncWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "masterDataSyncIdentityId");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_AreServiceBusAsTheMasterDataSyncIdentity_NotAKey_AndNothingElse()
    {
        var connections = MasterDataSyncWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Should().BeEmpty("the Service Bus connection is a managed one");
    }

    [Fact]
    public void EveryServiceBusStep_ReadsTheOneSubscriptionThroughTheManagedConnection()
    {
        var steps = MasterDataSyncWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection").ToList();

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>() == "servicebus"
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'master-data-events'", StringComparison.Ordinal)
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'master-data-sync'", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheIdentity_ForTheTokenStoreAndForServiceBus()
    {
        var connections = MasterDataSyncWorkflow.ConnectionsFile["managedApiConnections"]!.AsObject();
        var serviceBus = connections["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("servicebus");
        serviceBus["api"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_API_ID')");
        serviceBus["connection"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_ID')");
        serviceBus["connectionRuntimeUrl"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_RUNTIME_URL')");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('MASTER_DATA_SYNC_IDENTITY_ID')");
        }

        serviceBus["connectionProperties"]!["authentication"]!["audience"]!.GetValue<string>().Should().Be("https://servicebus.azure.net");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = MasterDataSyncWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }

    [Fact]
    public void TheSubscription_AdmitsOnlyTheShipperEvent_AndAResubmitToItself()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var filter = messaging[messaging.IndexOf("resource masterDataSyncFilter", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}\n", StringComparison.Ordinal)];

        filter.Should().Contain("(eventType = \\'Shipper\\' AND resubmitTo IS NULL) OR resubmitTo = \\'master-data-sync\\'");
    }

    [Fact]
    public void TheMasterDataSyncIdentity_HoldsTheTmsReplaceRole_AndNothingElse()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grants = identities.Split("\n\n").Where(block => block.Contains("principalId: masterDataSyncIdentity.properties.principalId", StringComparison.Ordinal) && block.Contains("appRoleAssignedTo", StringComparison.Ordinal)).ToList();

        grants.Should().ContainSingle();
        grants.Single().Should().Contain("resourceId: tmsApiPrincipal.id").And.Contain("appRoleId: masterDataReplaceRoleId");
    }
}
