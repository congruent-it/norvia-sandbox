using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.BillingLogicApp;

/// <summary>
/// The E1 Billing Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and E1. The Logic Apps runtime
/// runs only in Azure, so these tests read the definition. The ERP accepts the Canonical Model, so the map is the message itself, which
/// the sample tests check against the schema.
/// </summary>
public sealed class BillingWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_billable_line')?['correlationId']}";

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    private static JsonObject Parameters(string action) => BillingWorkflow.Action(action)["inputs"]!["queries"]!.AsObject();

    [Fact]
    public void Trigger_IsTheBillingSubscriptionOnTheTransportOrderEventsTopic_AndLocksTheMessage()
    {
        var inputs = BillingWorkflow.Trigger["inputs"]!;

        BillingWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        inputs["path"]!.GetValue<string>().Should().Contain("'transport-order-events'");
        inputs["path"]!.GetValue<string>().Should().Contain("'billing'");
        BillingWorkflow.Trigger["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
        inputs["path"]!.GetValue<string>().Should().EndWith("/messages/head/peek");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCorrelationId() =>
        BillingWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{coalesce(if(equals(length(string(coalesce(triggerBody(), ''))), 0), json('{}'), triggerBody())?['Properties']?['correlationId'], '00000000-0000-0000-0000-000000000000')}");

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        BillingWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        BillingWorkflow.Action(BillingWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        BillingWorkflow.Action(BillingWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        BillingWorkflow.Actions.Where(action => action.Key != BillingWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheEventTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = BillingWorkflow.Action(BillingWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "transportOrderNumber", "shipmentNumber", "eventType", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['shipperId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = BillingWorkflow.Action(BillingWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "transportOrderNumber", "shipmentNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(BillingWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStepsAfterTracking_TrackTheFlowByItsStableName_AndTheKeysTheyKnow()
    {
        var tracked = new[] { BillingWorkflow.Record, BillingWorkflow.DeadLetterRefused, BillingWorkflow.DeadLetterUnreadable }
            .Select(name => BillingWorkflow.Action(name)["trackedProperties"]!.AsObject()).ToList();

        tracked.Should().OnlyContain(properties => properties["flow"]!.GetValue<string>() == BillingWorkflow.Flow);
        tracked.Should().OnlyContain(properties => properties.Count >= 8);
        foreach (var properties in tracked)
        {
            properties.Select(property => property.Key).Should().Contain(["correlationId", "partyId", "transportOrderNumber", "shipmentNumber", "sourceMessageId", "outcome", "failureCode"]);
        }
    }

    [Fact]
    public void Check_RunsWhetherOrNotTheEventCouldBeRead_AndLooksOnlyAtTheTrackedOutputsSoItCantFail()
    {
        var check = BillingWorkflow.Action(BillingWorkflow.Check);

        Statuses(check, BillingWorkflow.Tracking).Should().BeEquivalentTo("Succeeded", "Failed");
        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("TransportOrderBillable").And.Contain("correlationId").And.Contain("transportOrderNumber").And.Contain("partyId");
        expression.Should().NotContain("triggerBody", "a message that isn't JSON would fail the check instead of being dead-lettered");
    }

    [Fact]
    public void UnreadableMessage_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = BillingWorkflow.Action(BillingWorkflow.DeadLetterUnreadable);
        var parameters = Parameters(BillingWorkflow.DeadLetterUnreadable);

        BillingWorkflow.Action(BillingWorkflow.Check)["else"]!["actions"]!.AsObject().Select(action => action.Key)
            .Should().Equal(BillingWorkflow.DeadLetterUnreadable, BillingWorkflow.FailTheUnreadableRun);
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("BillableLineUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent to the ERP");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("BillableLineUnreadable");
        BillingWorkflow.Action(BillingWorkflow.FailTheUnreadableRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void Record_PostsTheEventAsTheMessageCarriesIt_ToTheBillingApisBillableLinesEndpoint_WithTheCorrelationId()
    {
        var record = BillingWorkflow.Action(BillingWorkflow.Record);
        var inputs = record["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('erpBaseUrl')}/v1/billable-lines");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["body"]!.GetValue<string>().Should().Be("@json(base64ToString(triggerBody()?['ContentData']))", "the ERP accepts the Canonical Model, so there is nothing to map");
        record["runAfter"]!.AsObject().Should().BeEmpty("it is the first step of the check's branch for a readable event");
        BillingWorkflow.Action(BillingWorkflow.Check)["actions"]!.AsObject().Select(action => action.Key).Should().Equal(BillingWorkflow.Record, BillingWorkflow.Settle);
    }

    [Fact]
    public void Record_IsTheOnlyCall_AndAuthenticatesAsTheBillingIdentityForTheErpApisAudience()
    {
        var authentication = BillingWorkflow.Action(BillingWorkflow.Record)["inputs"]!["authentication"]!;

        authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        authentication["identity"]!.GetValue<string>().Should().Be("@parameters('billingIdentityId')");
        authentication["audience"]!.GetValue<string>().Should().Be("@parameters('erpAudience')");
        BillingWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().Equal(BillingWorkflow.Record);
    }

    [Fact]
    public void Record_RetriesWithTheBoundedExponentialPolicy_AsTheOtherLogicAppsDo()
    {
        // Logic Apps retries an exponential policy on a timeout, 408, 429 and a server error, and never on another client error, a conflict included.
        var retry = BillingWorkflow.Action(BillingWorkflow.Record)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Fact]
    public void Record_TracksTheOutcomeAndTheFailureCode_UnavailableForAnUnansweredCallAndTheErpsOwnCodeForARefusal()
    {
        var tracked = BillingWorkflow.Action(BillingWorkflow.Record)["trackedProperties"]!;

        tracked["outcome"]!.GetValue<string>().Should().Contain("'Succeeded'").And.Contain("'Failed'");
        var failureCode = tracked["failureCode"]!.GetValue<string>();
        failureCode.Should().Contain("'ErpUnavailable'").And.Contain("['body']?['code']").And.Contain("'ErpCallRefused'");
        failureCode.Should().Contain("408").And.Contain("429").And.Contain("500");
    }

    [Fact]
    public void Settle_RunsWhateverTheErpAnswered_AndCompletesTheMessageOnlyOnASuccess()
    {
        var settle = BillingWorkflow.Action(BillingWorkflow.Settle);

        Statuses(settle, BillingWorkflow.Record).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        settle["expression"]!.ToJsonString().Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
        settle["actions"]!.AsObject().Select(action => action.Key).Should().Equal(BillingWorkflow.Complete);
        var complete = BillingWorkflow.Action(BillingWorkflow.Complete);
        complete["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/complete");
        Parameters(BillingWorkflow.Complete)["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
    }

    [Fact]
    public void Settle_DeadLettersAClientErrorAConflictOrRetriesUsedUp_WithTheCodeAsReasonAndTheMessageAsDescription_AndFailsTheRun()
    {
        var settle = BillingWorkflow.Action(BillingWorkflow.Settle);
        var deadLetter = BillingWorkflow.Action(BillingWorkflow.DeadLetterRefused);
        var parameters = Parameters(BillingWorkflow.DeadLetterRefused);

        settle["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Equal(BillingWorkflow.DeadLetterRefused, BillingWorkflow.FailTheRefusedRun);
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Contain("['body']?['code']").And.Contain("'ErpUnavailable'").And.Contain("'ErpCallRefused'");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("['body']?['detail']").And.Contain("['body']?['title']");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("@action()?['inputs']?['parameters']?['deadLetterReason']");
        BillingWorkflow.Action(BillingWorkflow.FailTheRefusedRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void EveryServiceBusStep_SettlesTheMessageByItsLockToken()
    {
        var steps = BillingWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection" && operation.Name != "trigger").ToList();

        steps.Select(step => step.Name).Should().BeEquivalentTo(BillingWorkflow.Complete, BillingWorkflow.DeadLetterRefused, BillingWorkflow.DeadLetterUnreadable);
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["queries"]!["lockToken"]!.GetValue<string>() == "@triggerBody()?['LockToken']");
    }

    [Fact]
    public void NoStepHidesItsBodies_BecauseNothingInTheWorkflowIsASecret() =>
        BillingWorkflow.Operations().Should().OnlyContain(operation => operation.Operation["runtimeConfiguration"] == null || operation.Operation["runtimeConfiguration"]!["secureData"] == null);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        BillingWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = BillingWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("erpBaseUrl", "erpAudience", "billingIdentityId");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_AreServiceBusAsTheBillingIdentity_NotAKey_AndNothingElse()
    {
        var connections = BillingWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Should().BeEmpty("the Service Bus connection is a managed one");
    }

    [Fact]
    public void EveryServiceBusStep_ReadsTheOneSubscriptionThroughTheManagedConnection()
    {
        var steps = BillingWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection").ToList();

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>() == "servicebus"
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'transport-order-events'", StringComparison.Ordinal)
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'billing'", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheIdentity_ForTheTokenStoreAndForServiceBus()
    {
        var connections = BillingWorkflow.ConnectionsFile["managedApiConnections"]!.AsObject();
        var serviceBus = connections["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("servicebus");
        serviceBus["api"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_API_ID')");
        serviceBus["connection"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_ID')");
        serviceBus["connectionRuntimeUrl"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_RUNTIME_URL')");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('BILLING_IDENTITY_ID')");
        }

        serviceBus["connectionProperties"]!["authentication"]!["audience"]!.GetValue<string>().Should().Be("https://servicebus.azure.net");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = BillingWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }

    [Fact]
    public void TheSubscription_AdmitsOnlyTransportOrderBillable_AndAResubmitToItself()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var filter = messaging[messaging.IndexOf("resource billingFilter", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}\n", StringComparison.Ordinal)];

        filter.Should().Contain("(eventType = \\'TransportOrderBillable\\' AND resubmitTo IS NULL) OR resubmitTo = \\'billing\\'");
    }

    [Fact]
    public void TheBillingIdentity_HoldsTheErpRecordRole_AndNothingElse()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grants = identities.Split("\n\n").Where(block => block.Contains("principalId: billingIdentity.properties.principalId", StringComparison.Ordinal) && block.Contains("appRoleAssignedTo", StringComparison.Ordinal)).ToList();

        grants.Should().ContainSingle();
        grants.Single().Should().Contain("resourceId: erpApiPrincipal.id").And.Contain("appRoleId: billableLinesRecordRoleId");
    }
}
