using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierCostLogicApp;

/// <summary>
/// The P9 Carrier cost Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and E1. The Logic Apps runtime
/// runs only in Azure, so these tests read the definition. The ERP accepts the Canonical Model, so the map is the message itself, which
/// the sample tests check against the schema.
/// </summary>
public sealed class CarrierCostWorkflowTests
{
    private const string CorrelationId = "@{outputs('Track_the_carrier_cost')?['correlationId']}";

    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    private static JsonObject Parameters(string action) => CarrierCostWorkflow.Action(action)["inputs"]!["queries"]!.AsObject();

    [Fact]
    public void Trigger_IsTheCarrierCostSubscriptionOnTheShipmentEventsTopic_AndLocksTheMessage()
    {
        var inputs = CarrierCostWorkflow.Trigger["inputs"]!;

        CarrierCostWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        inputs["path"]!.GetValue<string>().Should().Contain("'shipment-events'");
        inputs["path"]!.GetValue<string>().Should().Contain("'carrier-cost'");
        CarrierCostWorkflow.Trigger["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>().Should().Be("servicebus");
        inputs["path"]!.GetValue<string>().Should().EndWith("/messages/head/peek");
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCorrelationId() =>
        CarrierCostWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{coalesce(if(equals(length(string(coalesce(triggerBody(), ''))), 0), json('{}'), triggerBody())?['Properties']?['correlationId'], '00000000-0000-0000-0000-000000000000')}");

    [Fact]
    public void Trigger_TracksNothing_BecauseTheFirstStepTracksTheKeysTheMessageCarries() =>
        CarrierCostWorkflow.Trigger["trackedProperties"].Should().BeNull();

    [Fact]
    public void Tracking_RunsFirst_AndOnlyItDoes()
    {
        CarrierCostWorkflow.Action(CarrierCostWorkflow.Tracking)["type"]!.GetValue<string>().Should().Be("Compose");
        CarrierCostWorkflow.Action(CarrierCostWorkflow.Tracking)["runAfter"]!.AsObject().Should().BeEmpty();
        CarrierCostWorkflow.Actions.Where(action => action.Key != CarrierCostWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
    }

    [Fact]
    public void Tracking_TakesTheKeysFromTheEventTheMessageCarries_LeavingOutAMissingOne()
    {
        // An interpolated expression would turn a missing field into an empty key; the contract leaves it out.
        var inputs = CarrierCostWorkflow.Action(CarrierCostWorkflow.Tracking)["inputs"]!.AsObject();

        inputs.Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "transportOrderNumber", "carrierBookingNumber", "shipmentNumber", "eventType", "sourceMessageId");
        inputs.Select(input => input.Value!.GetValue<string>()).Should().OnlyContain(
            expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal) && !expression.Contains("@{", StringComparison.Ordinal));
        inputs["partyId"]!.GetValue<string>().Should().EndWith("?['carrierId']");
        inputs["sourceMessageId"]!.GetValue<string>().Should().EndWith("?['eventId']");
    }

    [Fact]
    public void Tracking_TrackedProperties_AreTheKeysTheMessageKnows()
    {
        var tracked = CarrierCostWorkflow.Action(CarrierCostWorkflow.Tracking)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "transportOrderNumber", "carrierBookingNumber", "shipmentNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(CarrierCostWorkflow.Flow);
        tracked.Where(property => property.Key != "flow").Select(property => property.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@action()?['inputs']?['", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStepsAfterTracking_TrackTheFlowByItsStableName_AndTheKeysTheyKnow()
    {
        var tracked = new[] { CarrierCostWorkflow.Record, CarrierCostWorkflow.DeadLetterRefused, CarrierCostWorkflow.DeadLetterUnreadable }
            .Select(name => CarrierCostWorkflow.Action(name)["trackedProperties"]!.AsObject()).ToList();

        tracked.Should().OnlyContain(properties => properties["flow"]!.GetValue<string>() == CarrierCostWorkflow.Flow);
        tracked.Should().OnlyContain(properties => properties.Count >= 9);
        foreach (var properties in tracked)
        {
            properties.Select(property => property.Key).Should().Contain(["correlationId", "partyId", "transportOrderNumber", "carrierBookingNumber", "shipmentNumber", "sourceMessageId", "outcome", "failureCode"]);
        }
    }

    [Fact]
    public void Check_RunsWhetherOrNotTheEventCouldBeRead_AndLooksOnlyAtTheTrackedOutputsSoItCantFail()
    {
        var check = CarrierCostWorkflow.Action(CarrierCostWorkflow.Check);

        Statuses(check, CarrierCostWorkflow.Tracking).Should().BeEquivalentTo("Succeeded", "Failed");
        var expression = check["expression"]!.ToJsonString();
        expression.Should().Contain("CarrierCostIncurred").And.Contain("correlationId").And.Contain("carrierBookingNumber").And.Contain("partyId");
        expression.Should().NotContain("triggerBody", "a message that isn't JSON would fail the check instead of being dead-lettered");
    }

    [Fact]
    public void UnreadableMessage_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = CarrierCostWorkflow.Action(CarrierCostWorkflow.DeadLetterUnreadable);
        var parameters = Parameters(CarrierCostWorkflow.DeadLetterUnreadable);

        CarrierCostWorkflow.Action(CarrierCostWorkflow.Check)["else"]!["actions"]!.AsObject().Select(action => action.Key)
            .Should().Equal(CarrierCostWorkflow.DeadLetterUnreadable, CarrierCostWorkflow.FailTheUnreadableRun);
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Be("CarrierCostUnreadable");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent to the ERP");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("CarrierCostUnreadable");
        CarrierCostWorkflow.Action(CarrierCostWorkflow.FailTheUnreadableRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void Record_PostsTheEventAsTheMessageCarriesIt_ToTheCarrierCostApisCarrierCostsEndpoint_WithTheCorrelationId()
    {
        var record = CarrierCostWorkflow.Action(CarrierCostWorkflow.Record);
        var inputs = record["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('erpBaseUrl')}/v1/carrier-costs");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be(CorrelationId);
        inputs["body"]!.GetValue<string>().Should().Be("@json(base64ToString(triggerBody()?['ContentData']))", "the ERP accepts the Canonical Model, so there is nothing to map");
        record["runAfter"]!.AsObject().Should().BeEmpty("it is the first step of the check's branch for a readable event");
        CarrierCostWorkflow.Action(CarrierCostWorkflow.Check)["actions"]!.AsObject().Select(action => action.Key).Should().Equal(CarrierCostWorkflow.Record, CarrierCostWorkflow.Settle);
    }

    [Fact]
    public void Record_IsTheOnlyCall_AndAuthenticatesAsTheCarrierCostIdentityForTheErpApisAudience()
    {
        var authentication = CarrierCostWorkflow.Action(CarrierCostWorkflow.Record)["inputs"]!["authentication"]!;

        authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        authentication["identity"]!.GetValue<string>().Should().Be("@parameters('carrierCostIdentityId')");
        authentication["audience"]!.GetValue<string>().Should().Be("@parameters('erpAudience')");
        CarrierCostWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Select(operation => operation.Name)
            .Should().Equal(CarrierCostWorkflow.Record);
    }

    [Fact]
    public void Record_RetriesWithTheBoundedExponentialPolicy_AsTheOtherLogicAppsDo()
    {
        // Logic Apps retries an exponential policy on a timeout, 408, 429 and a server error, and never on another client error, a conflict included.
        var retry = CarrierCostWorkflow.Action(CarrierCostWorkflow.Record)["inputs"]!["retryPolicy"]!;

        retry["type"]!.GetValue<string>().Should().Be("exponential");
        retry["count"]!.GetValue<int>().Should().Be(3);
        retry["interval"]!.GetValue<string>().Should().Be("PT5S");
        retry["minimumInterval"]!.GetValue<string>().Should().Be("PT5S");
        retry["maximumInterval"]!.GetValue<string>().Should().Be("PT20S");
    }

    [Fact]
    public void Record_TracksTheOutcomeAndTheFailureCode_UnavailableForAnUnansweredCallAndTheErpsOwnCodeForARefusal()
    {
        var tracked = CarrierCostWorkflow.Action(CarrierCostWorkflow.Record)["trackedProperties"]!;

        tracked["outcome"]!.GetValue<string>().Should().Contain("'Succeeded'").And.Contain("'Failed'");
        var failureCode = tracked["failureCode"]!.GetValue<string>();
        failureCode.Should().Contain("'ErpUnavailable'").And.Contain("['body']?['code']").And.Contain("'ErpCallRefused'");
        failureCode.Should().Contain("408").And.Contain("429").And.Contain("500");
    }

    [Fact]
    public void Settle_RunsWhateverTheErpAnswered_AndCompletesTheMessageOnlyOnASuccess()
    {
        var settle = CarrierCostWorkflow.Action(CarrierCostWorkflow.Settle);

        Statuses(settle, CarrierCostWorkflow.Record).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        settle["expression"]!.ToJsonString().Should().Contain("greaterOrEquals").And.Contain("200").And.Contain("less").And.Contain("300");
        settle["actions"]!.AsObject().Select(action => action.Key).Should().Equal(CarrierCostWorkflow.Complete);
        var complete = CarrierCostWorkflow.Action(CarrierCostWorkflow.Complete);
        complete["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/complete");
        Parameters(CarrierCostWorkflow.Complete)["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
    }

    [Fact]
    public void Settle_DeadLettersAClientErrorAConflictOrRetriesUsedUp_WithTheCodeAsReasonAndTheMessageAsDescription_AndFailsTheRun()
    {
        var settle = CarrierCostWorkflow.Action(CarrierCostWorkflow.Settle);
        var deadLetter = CarrierCostWorkflow.Action(CarrierCostWorkflow.DeadLetterRefused);
        var parameters = Parameters(CarrierCostWorkflow.DeadLetterRefused);

        settle["else"]!["actions"]!.AsObject().Select(action => action.Key).Should().Equal(CarrierCostWorkflow.DeadLetterRefused, CarrierCostWorkflow.FailTheRefusedRun);
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        parameters["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Contain("['body']?['code']").And.Contain("'ErpUnavailable'").And.Contain("'ErpCallRefused'");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("['body']?['detail']").And.Contain("['body']?['title']");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        deadLetter["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Be("@action()?['inputs']?['parameters']?['deadLetterReason']");
        CarrierCostWorkflow.Action(CarrierCostWorkflow.FailTheRefusedRun)["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void EveryServiceBusStep_SettlesTheMessageByItsLockToken()
    {
        var steps = CarrierCostWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection" && operation.Name != "trigger").ToList();

        steps.Select(step => step.Name).Should().BeEquivalentTo(CarrierCostWorkflow.Complete, CarrierCostWorkflow.DeadLetterRefused, CarrierCostWorkflow.DeadLetterUnreadable);
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["queries"]!["lockToken"]!.GetValue<string>() == "@triggerBody()?['LockToken']");
    }

    [Fact]
    public void NoStepHidesItsBodies_BecauseNothingInTheWorkflowIsASecret() =>
        CarrierCostWorkflow.Operations().Should().OnlyContain(operation => operation.Operation["runtimeConfiguration"] == null || operation.Operation["runtimeConfiguration"]!["secureData"] == null);

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        CarrierCostWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = CarrierCostWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("erpBaseUrl", "erpAudience", "carrierCostIdentityId");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(
            value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_AreServiceBusAsTheCarrierCostIdentity_NotAKey_AndNothingElse()
    {
        var connections = CarrierCostWorkflow.ConnectionsFile["serviceProviderConnections"]!.AsObject();

        connections.Should().BeEmpty("the Service Bus connection is a managed one");
    }

    [Fact]
    public void EveryServiceBusStep_ReadsTheOneSubscriptionThroughTheManagedConnection()
    {
        var steps = CarrierCostWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection").ToList();

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["host"]!["connection"]!["referenceName"]!.GetValue<string>() == "servicebus"
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'shipment-events'", StringComparison.Ordinal)
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'carrier-cost'", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheIdentity_ForTheTokenStoreAndForServiceBus()
    {
        var connections = CarrierCostWorkflow.ConnectionsFile["managedApiConnections"]!.AsObject();
        var serviceBus = connections["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("servicebus");
        serviceBus["api"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_API_ID')");
        serviceBus["connection"]!["id"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_ID')");
        serviceBus["connectionRuntimeUrl"]!.GetValue<string>().Should().Be("@appsetting('SERVICE_BUS_CONNECTION_RUNTIME_URL')");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('CARRIER_COST_IDENTITY_ID')");
        }

        serviceBus["connectionProperties"]!["authentication"]!["audience"]!.GetValue<string>().Should().Be("https://servicebus.azure.net");
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway()
    {
        var excluded = CarrierCostWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>();

        excluded.Split(';').Should().Contain(["Request", "Exception"]);
    }

    [Fact]
    public void TheSubscription_AdmitsOnlyCarrierCostIncurred_AndAResubmitToItself()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var filter = messaging[messaging.IndexOf("resource carrierCostFilter", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}\n", StringComparison.Ordinal)];

        filter.Should().Contain("(eventType = \\'CarrierCostIncurred\\' AND resubmitTo IS NULL) OR resubmitTo = \\'carrier-cost\\'");
    }

    [Fact]
    public void TheCarrierCostIdentity_HoldsTheErpRecordRole_AndNothingElse()
    {
        var identities = Contracts.Read("Estate/identities.bicep");
        var grants = identities.Split("\n\n").Where(block => block.Contains("principalId: carrierCostIdentity.properties.principalId", StringComparison.Ordinal)).ToList();

        grants.Should().ContainSingle();
        grants.Single().Should().Contain("resourceId: erpApiPrincipal.id").And.Contain("appRoleId: carrierCostsRecordRoleId");
    }
}
