using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.TrackingLogicApp;

/// <summary>
/// The Carrier-tracking Logic App's workflow definition keeps to the tracking contract (coding guidelines §7), ADR 0008
/// and P5. The Logic Apps runtime runs only in Azure, so these tests read the definition; the map it calls and the
/// outcome it works out are tested on their own, and the local worker that plays it checks the same list.
/// </summary>
public sealed class TrackingWorkflowTests
{
    private static JsonObject Inputs(string action) => TrackingWorkflow.Action(action)["inputs"]!.AsObject();

    private static string OperationId(string action) => Inputs(action)["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>();

    [Fact]
    public void Trigger_IsTheTrackingQueue_AndItsClientTrackingIdIsTheCorrelationId()
    {
        TrackingWorkflow.Trigger["inputs"]!["parameters"]!["queueName"]!.GetValue<string>().Should().Be("carrier-tracking");
        TrackingWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("peekLockQueueMessages");
        TrackingWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['userProperties']?['correlationId']}");
    }

    [Fact]
    public void Tracking_RunsFirst_TakingTheApplicationPropertiesAsTrackedProperties()
    {
        var tracking = TrackingWorkflow.Action(TrackingWorkflow.Tracking);

        tracking["runAfter"]!.AsObject().Should().BeEmpty();
        TrackingWorkflow.Actions.Where(action => action.Key != TrackingWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0, "only the tracking step runs first");
        tracking["inputs"]!.AsObject().Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "sourceMessageId");
        tracking["trackedProperties"]!.AsObject().Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "sourceMessageId");
        tracking["trackedProperties"]!["flow"]!.GetValue<string>().Should().Be(TrackingWorkflow.Flow);
    }

    [Fact]
    public void TmsCall_IsTheOnlyStepThatCallsTheTms_AsTheTrackingIdentity_WithTheCorrelationId_AndNoRetryOfItsOwn()
    {
        var call = Inputs(TrackingWorkflow.TmsCall);

        call["uri"]!.GetValue<string>().Should().Be("@{parameters('tmsBaseUrl')}/v1/tracking-events");
        call["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{outputs('Track_the_event')?['correlationId']}");
        call["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('trackingIdentityId')");
        call["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
        call["retryPolicy"]!["type"]!.GetValue<string>().Should().Be("none", "a server error or timeout is retried from the queue");
        TrackingWorkflow.Operations().Count(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Should().Be(1);
    }

    [Fact]
    public void TmsCall_TracksTheBusinessKeysAndTheOutcome()
    {
        var tracked = TrackingWorkflow.Action(TrackingWorkflow.TmsCall)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo(
            "correlationId", "flow", "partyId", "sourceMessageId", "shipmentNumber", "carrierBookingNumber", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(TrackingWorkflow.Flow);
    }

    [Fact]
    public void Map_RunsStraightAfterTheTrackingStep_AndAFailureOfItFailsTheRunAfterDeadLetteringTheMessage()
    {
        TrackingWorkflow.Action(TrackingWorkflow.Map)["runAfter"]!.AsObject().Select(after => after.Key).Should().BeEquivalentTo(TrackingWorkflow.Tracking);
        var deadLetter = TrackingWorkflow.Action("Dead_letter_the_unmappable_message");
        var end = TrackingWorkflow.Action("Fail_the_run_on_the_unmappable_message");

        deadLetter["runAfter"]![TrackingWorkflow.Map]!.AsArray().Select(status => status!.GetValue<string>()).Should().BeEquivalentTo("Failed");
        OperationId("Dead_letter_the_unmappable_message").Should().Be("deadLetterQueueMessage");
        deadLetter["inputs"]!["parameters"]!["deadLetterReason"]!.GetValue<string>().Should().Be("UnmappableTrackingMessage");
        deadLetter["inputs"]!["parameters"]!["deadLetterErrorDescription"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        end["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void TmsCall_RunsOnlyOnAMappedEvent()
    {
        TrackingWorkflow.Action(TrackingWorkflow.TmsCall)["runAfter"]![TrackingWorkflow.Map]!.AsArray().Select(status => status!.GetValue<string>())
            .Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void Settle_CompletesTheMessage_OnlyWhenTheTmsRecordedTheEvent()
    {
        var settle = TrackingWorkflow.Action(TrackingWorkflow.Settle);

        settle["runAfter"]![TrackingWorkflow.TmsCall]!.AsArray().Select(status => status!.GetValue<string>()).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        settle["expression"]!.ToJsonString().Should().Contain("200").And.Contain("300");
        OperationId("Complete_the_message").Should().Be("completeQueueMessage");
        settle["actions"]!.AsObject().Select(action => action.Key).Should().BeEquivalentTo("Complete_the_message");
    }

    [Fact]
    public void Settle_DeadLettersAClientError_WithTheFailureCodeAsReasonAndTheMessageAsDescription_NotRetried()
    {
        var deadLetter = TrackingWorkflow.Action("Dead_letter_the_refused_message");
        var parameters = deadLetter["inputs"]!["parameters"]!;

        OperationId("Dead_letter_the_refused_message").Should().Be("deadLetterQueueMessage");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Contain("['code']").And.Contain("TmsCallRefused");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("['detail']");
        TrackingWorkflow.Action("Settle_the_refused_or_unreached_message")["expression"]!.ToJsonString()
            .Should().Contain("408").And.Contain("429").And.Contain("500", "those are retried, not dead-lettered");
        TrackingWorkflow.Action("Fail_the_run_on_the_refused_message")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void Settle_AbandonsAServerErrorOrTimeout_SoTheQueueRetriesItUpToItsMaximumDeliveryCount()
    {
        OperationId("Abandon_the_message_to_retry").Should().Be("abandonQueueMessage");
        TrackingWorkflow.Action("Abandon_the_message_to_retry")["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("Retrying");
        TrackingWorkflow.Action("Fail_the_run_on_the_unreached_TMS")["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("TmsUnavailable");
    }

    [Fact]
    public void EveryServiceBusStep_SettlesTheMessageByItsLockToken()
    {
        var steps = TrackingWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ServiceProvider" && operation.Name != "trigger").ToList();

        steps.Should().HaveCount(4);
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["parameters"]!["lockToken"]!.GetValue<string>() == "@triggerBody()?['lockToken']");
    }

    [Fact]
    public void Parameters_AreTheTmsAndTheTrackingIdentity()
    {
        TrackingWorkflow.ParametersFile.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "trackingIdentityId");
    }

    [Fact]
    public void Host_LeavesRequestAndExceptionOutOfSampling()
    {
        TrackingWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>().Should().Be("Request;Exception");
    }

    [Fact]
    public void TheWorkflow_IsStateful_SoItsRunHistoryIsKept()
    {
        TrackingWorkflow.Kind.Should().Be("Stateful");
    }

    [Fact]
    public void TheOutcomeExpressions_AgreeWithTmsTrackingOutcome()
    {
        // The workflow and the local worker settle by the same rule: 2xx completes, 4xx but 408 and 429 dead-letters, the rest retries.
        var tracked = TrackingWorkflow.Action(TrackingWorkflow.TmsCall)["trackedProperties"]!["outcome"]!.GetValue<string>();

        tracked.Should().Contain("'Succeeded'").And.Contain("'Retrying'").And.Contain("'DeadLettered'");
        TmsTrackingOutcome.Of(200, "{}").Outcome.Should().Be("Succeeded");
        TmsTrackingOutcome.Of(503, "").Outcome.Should().Be("Retrying");
        TmsTrackingOutcome.Of(409, "{}").Outcome.Should().Be("DeadLettered");
    }
}
