using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ProofOfDeliveryLogicApp;

/// <summary>
/// The Carrier Proof of delivery Logic App's workflow definition keeps to the tracking contract (coding guidelines §7),
/// ADR 0009 and P6. The Logic Apps runtime runs only in Azure, so these tests read the definition; the map it calls and
/// the outcome it works out are tested on their own, and the local worker that plays it checks the same list.
/// </summary>
public sealed class ProofOfDeliveryWorkflowTests
{
    private static JsonObject Inputs(string action) => ProofOfDeliveryWorkflow.Action(action)["inputs"]!.AsObject();

    private static string OperationId(string action) => Inputs(action)["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>();

    private static IEnumerable<string> RunsAfter(string action, string previous) =>
        ProofOfDeliveryWorkflow.Action(action)["runAfter"]![previous]!.AsArray().Select(status => status!.GetValue<string>());

    [Fact]
    public void Trigger_IsTheProofOfDeliveryQueue_AndItsClientTrackingIdIsTheCorrelationId()
    {
        ProofOfDeliveryWorkflow.Trigger["inputs"]!["parameters"]!["queueName"]!.GetValue<string>().Should().Be("carrier-proof-of-delivery");
        ProofOfDeliveryWorkflow.Trigger["inputs"]!["serviceProviderConfiguration"]!["operationId"]!.GetValue<string>().Should().Be("peekLockQueueMessages");
        ProofOfDeliveryWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['body']?['userProperties']?['correlationId']}");
    }

    [Fact]
    public void Tracking_RunsFirst_TakingTheApplicationPropertiesAsTrackedProperties()
    {
        var tracking = ProofOfDeliveryWorkflow.Action(ProofOfDeliveryWorkflow.Tracking);

        tracking["runAfter"]!.AsObject().Should().BeEmpty();
        ProofOfDeliveryWorkflow.Actions.Where(action => action.Key != ProofOfDeliveryWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0, "only the tracking step runs first");
        tracking["inputs"]!.AsObject().Select(input => input.Key).Should().BeEquivalentTo("correlationId", "partyId", "sourceMessageId", "blobName");
        tracking["trackedProperties"]!.AsObject().Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId", "sourceMessageId");
        tracking["trackedProperties"]!["flow"]!.GetValue<string>().Should().Be(ProofOfDeliveryWorkflow.Flow);
    }

    [Fact]
    public void ReadTheBlob_FetchesTheReferencedBlobFromTheInboundContainer()
    {
        var read = Inputs("Read_the_blob");

        OperationId("Read_the_blob").Should().Be("readBlob");
        read["parameters"]!["containerName"]!.GetValue<string>().Should().Be("@parameters('inboundContainer')");
        read["parameters"]!["blobName"]!.GetValue<string>().Should().Contain("blobName");
        RunsAfter("Read_the_blob", ProofOfDeliveryWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void AMissingBlob_DeadLettersTheMessageAndFailsTheRun_KeepingNothingToDelete()
    {
        var deadLetter = ProofOfDeliveryWorkflow.Action("Dead_letter_the_missing_blob");

        RunsAfter("Dead_letter_the_missing_blob", "Read_the_blob").Should().BeEquivalentTo("Failed");
        OperationId("Dead_letter_the_missing_blob").Should().Be("deadLetterQueueMessage");
        deadLetter["inputs"]!["parameters"]!["deadLetterReason"]!.GetValue<string>().Should().Be("ProofOfDeliveryBlobMissing");
        deadLetter["inputs"]!["parameters"]!["deadLetterErrorDescription"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        ProofOfDeliveryWorkflow.Action("Fail_the_run_on_the_missing_blob")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void Map_RunsOnAReadBlob_AndAFailureOfItFailsTheRunAfterDeadLetteringTheMessage()
    {
        RunsAfter(ProofOfDeliveryWorkflow.Map, "Read_the_blob").Should().BeEquivalentTo("Succeeded");
        ProofOfDeliveryWorkflow.Action(ProofOfDeliveryWorkflow.Map)["inputs"]!["parameters"]!["carrierId"]!.GetValue<string>().Should().Contain("partyId");
        var deadLetter = ProofOfDeliveryWorkflow.Action("Dead_letter_the_unmappable_message");

        RunsAfter("Dead_letter_the_unmappable_message", ProofOfDeliveryWorkflow.Map).Should().BeEquivalentTo("Failed");
        OperationId("Dead_letter_the_unmappable_message").Should().Be("deadLetterQueueMessage");
        deadLetter["inputs"]!["parameters"]!["deadLetterReason"]!.GetValue<string>().Should().Be("UnmappableProofOfDelivery");
        ProofOfDeliveryWorkflow.Action("Fail_the_run_on_the_unmappable_message")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void TmsCall_IsTheOnlyHttpStep_AMultipartCallAsTheProofOfDeliveryIdentity_WithTheCorrelationId_AndNoRetryOfItsOwn()
    {
        var call = Inputs(ProofOfDeliveryWorkflow.TmsCall);

        call["uri"]!.GetValue<string>().Should().Be("@{parameters('tmsBaseUrl')}/v1/proofs-of-delivery");
        call["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_proof_of_delivery'))", "the map returns the multipart body, metadata and files in one call");
        call["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{outputs('Track_the_message')?['correlationId']}");
        call["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('proofOfDeliveryIdentityId')");
        call["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
        call["retryPolicy"]!["type"]!.GetValue<string>().Should().Be("none", "a server error or timeout is retried from the queue");
        RunsAfter(ProofOfDeliveryWorkflow.TmsCall, ProofOfDeliveryWorkflow.Map).Should().BeEquivalentTo("Succeeded");
        ProofOfDeliveryWorkflow.Operations().Count(operation => operation.Operation["type"]!.GetValue<string>() == "Http").Should().Be(1);
    }

    [Fact]
    public void TmsCall_TracksTheBusinessKeysAndTheOutcome()
    {
        var tracked = ProofOfDeliveryWorkflow.Action(ProofOfDeliveryWorkflow.TmsCall)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo(
            "correlationId", "flow", "partyId", "sourceMessageId", "shipmentNumber", "carrierBookingNumber", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(ProofOfDeliveryWorkflow.Flow);
    }

    [Fact]
    public void Settle_DeletesTheBlobOnlyWhenTheTmsAccepted_AndOnlyThenCompletesTheMessage()
    {
        var settle = ProofOfDeliveryWorkflow.Action(ProofOfDeliveryWorkflow.Settle);

        RunsAfter(ProofOfDeliveryWorkflow.Settle, ProofOfDeliveryWorkflow.TmsCall).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        settle["expression"]!.ToJsonString().Should().Contain("200").And.Contain("300");
        OperationId("Delete_the_blob").Should().Be("deleteBlob");
        Inputs("Delete_the_blob")["parameters"]!["containerName"]!.GetValue<string>().Should().Be("@parameters('inboundContainer')");
        OperationId("Complete_the_message").Should().Be("completeQueueMessage");
        RunsAfter("Complete_the_message", "Delete_the_blob").Should().BeEquivalentTo("Succeeded");
        settle["actions"]!.AsObject().Select(action => action.Key).Should().BeEquivalentTo("Delete_the_blob", "Complete_the_message");
    }

    [Fact]
    public void Settle_DeadLettersAClientError_WithTheFailureCodeAsReasonAndTheMessageAsDescription_NotRetried_AndKeepsTheBlob()
    {
        var deadLetter = ProofOfDeliveryWorkflow.Action("Dead_letter_the_refused_message");
        var parameters = deadLetter["inputs"]!["parameters"]!;

        OperationId("Dead_letter_the_refused_message").Should().Be("deadLetterQueueMessage");
        parameters["deadLetterReason"]!.GetValue<string>().Should().Contain("['code']").And.Contain("TmsCallRefused");
        parameters["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("['detail']");
        ProofOfDeliveryWorkflow.Action("Settle_the_refused_or_unreached_message")["expression"]!.ToJsonString()
            .Should().Contain("408").And.Contain("429").And.Contain("500", "those are retried, not dead-lettered");
        ProofOfDeliveryWorkflow.Action("Fail_the_run_on_the_refused_message")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
        ProofOfDeliveryWorkflow.Operations().Where(operation => operation.Name == "Delete_the_blob").Should().HaveCount(1, "only the accepted branch deletes it");
    }

    [Fact]
    public void Settle_AbandonsAServerErrorOrTimeout_SoTheQueueRetriesItUpToItsMaximumDeliveryCount()
    {
        OperationId("Abandon_the_message_to_retry").Should().Be("abandonQueueMessage");
        ProofOfDeliveryWorkflow.Action("Abandon_the_message_to_retry")["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("Retrying");
        ProofOfDeliveryWorkflow.Action("Fail_the_run_on_the_unreached_TMS")["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("TmsUnavailable");
    }

    [Fact]
    public void EveryServiceBusSettlingStep_SettlesTheMessageByItsLockToken()
    {
        var steps = ProofOfDeliveryWorkflow.Operations()
            .Where(operation => operation.Name != "trigger" && operation.Operation["inputs"]?["serviceProviderConfiguration"]?["connectionName"]?.GetValue<string>() == "serviceBus")
            .ToList();

        steps.Should().HaveCount(5, "Dead_letter x3, Complete and Abandon");
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["parameters"]!["lockToken"]!.GetValue<string>() == "@triggerBody()?['lockToken']");
    }

    [Fact]
    public void NoStep_HasSecureInputsOrOutputs_SoTheOperatorSeesTheMessageAndTheBlobReference()
    {
        ProofOfDeliveryWorkflow.Operations().Should().OnlyContain(operation => operation.Operation["runtimeConfiguration"] == null);
        ProofOfDeliveryWorkflow.WorkflowText.Should().NotContain("secureData");
    }

    [Fact]
    public void Parameters_AreTheTmsTheProofOfDeliveryIdentityAndTheInboundContainer()
    {
        ProofOfDeliveryWorkflow.ParametersFile.Select(parameter => parameter.Key).Should().BeEquivalentTo(
            "tmsBaseUrl", "tmsAudience", "proofOfDeliveryIdentityId", "inboundContainer");
    }

    [Fact]
    public void Connections_ReachServiceBusAndTheInboundBlobsAsTheProofOfDeliveryIdentity()
    {
        var connections = Contracts.Read("ProofOfDeliveryLogicApp/connections.json");

        connections.Should().Contain("/serviceProviders/serviceBus").And.Contain("/serviceProviders/AzureBlob");
        connections.Should().Contain("@appsetting('PROOF_OF_DELIVERY_IDENTITY_ID')").And.NotContain("ConnectionString");
    }

    [Fact]
    public void Host_LeavesRequestAndExceptionOutOfSampling()
    {
        ProofOfDeliveryWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>().Should().Be("Request;Exception");
    }

    [Fact]
    public void TheWorkflow_IsStateful_SoItsRunHistoryIsKept()
    {
        ProofOfDeliveryWorkflow.Kind.Should().Be("Stateful");
    }

    [Fact]
    public void TheOutcomeExpressions_AgreeWithTmsProofOfDeliveryOutcome()
    {
        // The workflow and the local worker settle by the same rule: 2xx completes, 4xx but 408 and 429 dead-letters, the rest retries.
        var tracked = ProofOfDeliveryWorkflow.Action(ProofOfDeliveryWorkflow.TmsCall)["trackedProperties"]!["outcome"]!.GetValue<string>();

        tracked.Should().Contain("'Succeeded'").And.Contain("'Retrying'").And.Contain("'DeadLettered'");
        TmsProofOfDeliveryOutcome.Of(200, "{}").Outcome.Should().Be("Succeeded");
        TmsProofOfDeliveryOutcome.Of(503, "").Outcome.Should().Be("Retrying");
        TmsProofOfDeliveryOutcome.Of(409, "{}").Outcome.Should().Be("DeadLettered");
    }
}
