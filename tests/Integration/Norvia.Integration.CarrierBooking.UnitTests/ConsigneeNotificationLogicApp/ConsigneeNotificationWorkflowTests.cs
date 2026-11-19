using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.ConsigneeNotificationLogicApp;

/// <summary>
/// The P7 Consignee notification Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and to ADR 0052: the
/// Consignee's address and the tracking code never reach a tracked property, a log or a plain step output. The Logic Apps runtime runs
/// only in Azure, so these tests read the definition; the map it calls is tested on its own, and the local worker plays the same steps (ADR 0033).
/// </summary>
public sealed class ConsigneeNotificationWorkflowTests
{
    private static string[] Statuses(JsonObject action, string after) =>
        [.. action["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>())];

    private static string[] Secure(JsonObject action) =>
        [.. action["runtimeConfiguration"]!["secureData"]!["properties"]!.AsArray().Select(property => property!.GetValue<string>())];

    [Fact]
    public void Trigger_IsTheConsigneeNotificationSubscriptionOnTheShipmentEventsTopic()
    {
        var path = ConsigneeNotificationWorkflow.Trigger["inputs"]!["path"]!.GetValue<string>();

        ConsigneeNotificationWorkflow.Trigger["type"]!.GetValue<string>().Should().Be("ApiConnection");
        path.Should().Contain("'shipment-events'").And.Contain("'consignee-notification'").And.EndWith("/messages/head/peek");
        ConsigneeNotificationWorkflow.Trigger["trackedProperties"].Should().BeNull();
    }

    [Fact]
    public void Trigger_ClientTrackingId_IsTheNotificationsCorrelationId() =>
        ConsigneeNotificationWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Contain("?['Properties']?['correlationId']");

    [Fact]
    public void Tracking_RunsFirst_AndTracksOnlyKeysOfTheContract_NeverTheAddressOrACode()
    {
        var tracking = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.Tracking);

        tracking["runAfter"]!.AsObject().Should().BeEmpty();
        ConsigneeNotificationWorkflow.Actions.Where(action => action.Key != ConsigneeNotificationWorkflow.Tracking)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0);
        tracking["inputs"]!.AsObject().Select(input => input.Key).Should().BeEquivalentTo("correlationId", "shipmentNumber", "transportOrderNumber", "sourceMessageId");
        tracking["inputs"]!.AsObject().Select(input => input.Value!.GetValue<string>())
            .Should().OnlyContain(expression => expression.StartsWith("@json(base64ToString(triggerBody()?['ContentData']))?['", StringComparison.Ordinal));
        var tracked = tracking["trackedProperties"]!.AsObject();
        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "shipmentNumber", "transportOrderNumber", "sourceMessageId");
        tracked["flow"]!.GetValue<string>().Should().Be(ConsigneeNotificationWorkflow.Flow);
    }

    [Fact]
    public void Operations_EveryOneTheTrailNeedsTracksTheFlowByItsStableName()
    {
        var tracked = ConsigneeNotificationWorkflow.Operations().Where(operation => operation.Operation["trackedProperties"] is not null).ToList();

        tracked.Select(operation => operation.Operation["trackedProperties"]!["flow"]!.GetValue<string>()).Should().OnlyContain(flow => flow == ConsigneeNotificationWorkflow.Flow);
        tracked.Select(operation => operation.Name).Should().Contain([
            ConsigneeNotificationWorkflow.Tracking, ConsigneeNotificationWorkflow.ReadAction, ConsigneeNotificationWorkflow.GetContent, ConsigneeNotificationWorkflow.TrackTmsAnswer,
            ConsigneeNotificationWorkflow.MapEmail, ConsigneeNotificationWorkflow.Send, ConsigneeNotificationWorkflow.TrackEmail]);
    }

    [Fact]
    public void TrackedProperties_NeverHoldTheAddressTheCodeOrTheEmail()
    {
        var tracked = ConsigneeNotificationWorkflow.Operations()
            .Where(operation => operation.Operation["trackedProperties"] is not null)
            .SelectMany(operation => operation.Operation["trackedProperties"]!.AsObject().Select(property => property.Value?.ToString() ?? string.Empty));

        // A tracked property may name the failure ConsigneeEmailUnusable; it may not read the address, the code or the email from a step.
        tracked.Should().NotContain(expression => expression.Contains("?['email']", StringComparison.Ordinal)
            || expression.Contains("trackingCode", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("plainText", StringComparison.Ordinal)
            || expression.Contains("'Get_the_Consignee_address_and_link'", StringComparison.Ordinal)
            || expression.Contains("'Map_the_email'", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RunsTheConsigneeNotificationMapOnTheMessageContent_AndTracksAnUnreadableOneAsFailed()
    {
        var read = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.ReadAction);

        read["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        read["inputs"]!["functionName"]!.GetValue<string>().Should().Be("ReadConsigneeNotificationRequested");
        read["inputs"]!["parameters"]!["consigneeNotificationRequested"]!.GetValue<string>().Should().Be("@{triggerBody()?['ContentData']}");
        read["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("ConsigneeNotificationRequestedUnreadable");
        Statuses(read, ConsigneeNotificationWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void UnreadableNotification_IsDeadLetteredWithTheFailureCodeAndAReadableMessage_AndFailsTheRun()
    {
        var deadLetter = ConsigneeNotificationWorkflow.Action("Dead_letter_the_unreadable_notification");
        var queries = deadLetter["inputs"]!["queries"]!;

        Statuses(deadLetter, ConsigneeNotificationWorkflow.ReadAction).Should().BeEquivalentTo("Failed");
        deadLetter["inputs"]!["path"]!.GetValue<string>().Should().EndWith("/messages/deadletter");
        queries["lockToken"]!.GetValue<string>().Should().Be("@triggerBody()?['LockToken']");
        queries["deadLetterReason"]!.GetValue<string>().Should().Be("ConsigneeNotificationRequestedUnreadable");
        queries["deadLetterErrorDescription"]!.GetValue<string>().Should().Contain("nothing was sent");
        deadLetter["trackedProperties"]!["outcome"]!.GetValue<string>().Should().Be("DeadLettered");
        ConsigneeNotificationWorkflow.Action("Fail_the_run_on_the_unreadable_notification")["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public void ReadNotification_IsCompletedBeforeTheTmsIsAsked_SoAFailedRunIsNotRedelivered()
    {
        Statuses(ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.Complete), ConsigneeNotificationWorkflow.ReadAction).Should().BeEquivalentTo("Succeeded");
        Statuses(ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.GetContent), ConsigneeNotificationWorkflow.Complete).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void GetContent_AsksTheTmsForTheTransportOrdersAddressAndLink_AsTheIdentity_WithTheCorrelationId_AndItsOutputIsSecure()
    {
        var get = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.GetContent);
        var inputs = get["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("GET");
        inputs["uri"]!.GetValue<string>().Should().StartWith("@{parameters('tmsBaseUrl')}/v1/consignee-notifications/@{encodeURIComponent(json(body('Read_the_notification'))?['transportOrderNumber'])}");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{outputs('Track_the_notification')?['correlationId']}");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('consigneeNotificationIdentityId')");
        inputs["authentication"]!["audience"]!.GetValue<string>().Should().Be("@parameters('tmsAudience')");
        inputs["retryPolicy"]!["count"]!.GetValue<int>().Should().Be(3);
        Secure(get).Should().Equal("outputs");
    }

    [Fact]
    public void TmsAnswer_ANotFoundIsNothingToSendAndNoError_AndOnlyAServerSideOrRefusedCallFails()
    {
        var answer = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.TmsAnswer)["inputs"]!;

        Statuses(ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.TmsAnswer), ConsigneeNotificationWorkflow.GetContent).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        answer["nothingToSend"]!.GetValue<string>().Should().Contain(", 404)");
        answer["failureCode"]!.GetValue<string>().Should().Contain("equals(coalesce(actions('Get_the_Consignee_address_and_link')?['outputs']?['statusCode'], 0), 404)), null,")
            .And.Contain("'TmsUnavailable'").And.Contain("'TmsCallRefused'");
        ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.TrackTmsAnswer)["inputs"]!["outcome"]!.GetValue<string>().Should().Contain("'Succeeded', 'Failed'");
    }

    [Fact]
    public void Email_IsOnlyMappedAndSentWhenTheTmsGaveSomethingToSend()
    {
        var gate = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.SendIfAny);

        gate["expression"]!["equals"]![0]!.GetValue<string>().Should().Be("@outputs('Read_the_TMS_answer')?['somethingToSend']");
        gate["actions"]!.AsObject().Select(action => action.Key).Should().BeEquivalentTo(ConsigneeNotificationWorkflow.MapEmail, ConsigneeNotificationWorkflow.Send, ConsigneeNotificationWorkflow.TrackEmail);
        Statuses(gate, ConsigneeNotificationWorkflow.TrackTmsAnswer).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void MapEmail_RunsTheMapOnTheEventAndTheTmsAnswer_WithTheSecretsKeptOutOfTheRunHistory()
    {
        var map = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.MapEmail);
        var parameters = map["inputs"]!["parameters"]!;

        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapConsigneeEmail");
        parameters["consigneeNotificationRequested"]!.GetValue<string>().Should().Be("@{triggerBody()?['ContentData']}");
        parameters["consigneeNotificationContent"]!.GetValue<string>().Should().Be("@{string(body('Get_the_Consignee_address_and_link'))}");
        parameters["trackingBaseUrl"]!.GetValue<string>().Should().Be("@{parameters('partnerPortalBaseUrl')}");
        Secure(map).Should().BeEquivalentTo("inputs", "outputs");
        map["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain("ConsigneeEmailUnusable");
    }

    [Fact]
    public void Send_PostsTheEmailToTheEmailServiceAsTheIdentity_WithTheSecretsKeptOutOfTheRunHistory_AndRetriesBoundedly()
    {
        var send = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.Send);
        var inputs = send["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().StartWith("@{parameters('emailEndpoint')}/emails:send");
        inputs["body"]!["senderAddress"]!.GetValue<string>().Should().Be("@parameters('emailSenderAddress')");
        inputs["body"]!["recipients"]!["to"]![0]!["address"]!.GetValue<string>().Should().Be("@json(body('Map_the_email'))?['to']");
        inputs["authentication"]!["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
        inputs["authentication"]!["identity"]!.GetValue<string>().Should().Be("@parameters('consigneeNotificationIdentityId')");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{outputs('Track_the_notification')?['correlationId']}");
        inputs["retryPolicy"]!["type"]!.GetValue<string>().Should().Be("exponential");
        inputs["retryPolicy"]!["count"]!.GetValue<int>().Should().Be(3);
        Secure(send).Should().BeEquivalentTo("inputs", "outputs");
        Statuses(send, ConsigneeNotificationWorkflow.MapEmail).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void TrackEmail_RunsWhateverTheEmailServiceAnswered_AndTracksOnlyTheOutcomeAndTheFailureCode()
    {
        var track = ConsigneeNotificationWorkflow.Action(ConsigneeNotificationWorkflow.TrackEmail);

        Statuses(track, ConsigneeNotificationWorkflow.Send).Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut");
        track["inputs"]!["failureCode"]!.GetValue<string>().Should().Contain("'EmailServiceUnavailable'").And.Contain("'EmailRefused'");
        track["trackedProperties"]!.AsObject().Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "shipmentNumber", "transportOrderNumber", "outcome", "failureCode");
    }

    [Fact]
    public void TheShippersPath_IsNotInThisWorkflow_SoAFailedConsigneeNotificationCantBlockIt()
    {
        ConsigneeNotificationWorkflow.WorkflowText.Should().NotContain("shipper-notification").And.NotContain("report-sent").And.NotContain("shipper-notifications");
    }

    [Fact]
    public void Workflow_IsStateful_BecauseTheRunHistoryIsTheIntegrationOperatorsTrail() =>
        ConsigneeNotificationWorkflow.Kind.Should().Be("Stateful");

    [Fact]
    public void Parameters_AreAppSettings_NeverValues()
    {
        var parameters = ConsigneeNotificationWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "consigneeNotificationIdentityId", "partnerPortalBaseUrl", "emailEndpoint", "emailSenderAddress");
        parameters.Select(parameter => parameter.Value!["value"]!.GetValue<string>()).Should().OnlyContain(value => value.StartsWith("@appsetting('", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceBusConnection_IsAManagedConnectionSignedInAsTheConsigneeNotificationIdentity_AndEveryStepReadsTheOneSubscription()
    {
        var connections = ConsigneeNotificationWorkflow.ConnectionsFile;
        var serviceBus = connections["managedApiConnections"]!["servicebus"]!;

        connections.Select(connection => connection.Key).Should().BeEquivalentTo("managedApiConnections");
        foreach (var authentication in new[] { serviceBus["authentication"]!, serviceBus["connectionProperties"]!["authentication"]! })
        {
            authentication["type"]!.GetValue<string>().Should().Be("ManagedServiceIdentity");
            authentication["identity"]!.GetValue<string>().Should().Be("@appsetting('CONSIGNEE_NOTIFICATION_IDENTITY_ID')");
        }

        var steps = ConsigneeNotificationWorkflow.Operations().Where(operation => operation.Operation["type"]!.GetValue<string>() == "ApiConnection").ToList();
        steps.Should().OnlyContain(step => step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'shipment-events'", StringComparison.Ordinal)
            && step.Operation["inputs"]!["path"]!.GetValue<string>().Contains("'consignee-notification'", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_LeavesRequestsAndExceptionsOutOfSampling_SoNothingTrackedIsSampledAway() =>
        ConsigneeNotificationWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>().Split(';').Should().Contain(["Request", "Exception"]);
}
