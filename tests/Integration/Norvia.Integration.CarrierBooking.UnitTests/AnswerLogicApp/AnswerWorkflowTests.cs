using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.AnswerLogicApp;

/// <summary>
/// The Carrier-answer Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and P4. The
/// Logic Apps runtime runs only in Azure, so these tests read the definition; the map and the relay it calls are tested
/// on their own.
/// </summary>
public sealed class AnswerWorkflowTests
{
    private static IEnumerable<string> RunAfter(string action, string after) =>
        AnswerWorkflow.Action(action)["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>());

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCorrelationId()
    {
        AnswerWorkflow.Trigger["correlation"]?["clientTrackingId"]?.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
    }

    [Fact]
    public void Trigger_TracksNothing_BecauseLogicAppsDropsARequestTriggersTrackedProperties()
    {
        AnswerWorkflow.Trigger["trackedProperties"].Should().BeNull();
    }

    [Fact]
    public void CallerCheck_RunsFirst_AndRefusesACallerOtherThanApiManagement()
    {
        var check = AnswerWorkflow.Action(AnswerWorkflow.CallerCheck);

        check["runAfter"]!.AsObject().Should().BeEmpty();
        JsonNode.DeepEquals(check["expression"], new JsonObject
        {
            ["and"] = new JsonArray(new JsonObject
            {
                ["equals"] = new JsonArray("@triggerOutputs()?['headers']?['X-MS-CLIENT-PRINCIPAL-ID']", "@parameters('apiManagementPrincipalId')"),
            }),
        }).Should().BeTrue();
        AnswerWorkflow.Actions.Where(action => action.Key != AnswerWorkflow.CallerCheck)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0, "only the caller check runs first");
    }

    [Fact]
    public void CallerCheck_AnswersAnotherCaller403_FailsTheRun_AndTracksTheRefusal()
    {
        var refusal = AnswerWorkflow.Action(AnswerWorkflow.CallerCheck)["else"]!["actions"]!.AsObject();
        var answer = AnswerWorkflow.Action("Refuse_the_caller");
        var end = refusal.Select(action => action.Value!).Single(action => action["type"]!.GetValue<string>() == "Terminate");
        var tracked = answer["trackedProperties"]!.AsObject();

        answer["inputs"]!["statusCode"]!.GetValue<int>().Should().Be(403);
        end["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("CallerNotApiManagement");
        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(AnswerWorkflow.Flow);
        tracked["outcome"]!.GetValue<string>().Should().Be("Refused");
    }

    [Fact]
    public void Tracking_RunsStraightAfterTheCallerCheck_TakingTheCorrelationIdTheCarrierAndTheBookingFromTheCall()
    {
        var tracking = AnswerWorkflow.Action(AnswerWorkflow.Tracking);

        tracking["type"]!.GetValue<string>().Should().Be("Compose");
        RunAfter(AnswerWorkflow.Tracking, AnswerWorkflow.CallerCheck).Should().BeEquivalentTo("Succeeded");
        JsonNode.DeepEquals(tracking["inputs"], new JsonObject
        {
            ["correlationId"] = "@triggerOutputs()?['headers']?['X-Correlation-ID']",
            ["partyId"] = "@triggerOutputs()?['headers']?['X-Carrier-ID']",
            ["carrierBookingNumber"] = "@triggerBody()?['carrierBookingNumber']",
        }).Should().BeTrue();
        tracking["trackedProperties"]!.AsObject().Select(property => property.Key)
            .Should().BeEquivalentTo("correlationId", "flow", "partyId", "carrierBookingNumber");
        tracking["trackedProperties"]!["flow"]!.GetValue<string>().Should().Be(AnswerWorkflow.Flow);
    }

    [Fact]
    public void MapTheAnswer_GetsTheAnswerEncoded_AndTheCarrierFromTheHeaderAlone()
    {
        // The Functions runtime reads the fields of a parameter that holds JSON text as parameters too: an answer with a
        // carrierId field would then choose the Carrier (ADR 0005). Base64 text has no fields.
        var map = AnswerWorkflow.Action(AnswerWorkflow.Map);

        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapCarrierAnswer");
        JsonNode.DeepEquals(map["inputs"]!["parameters"], new JsonObject
        {
            ["carrierAnswer"] = "@{base64(triggerBody())}",
            ["carrierId"] = "@{triggerOutputs()?['headers']?['X-Carrier-ID']}",
        }).Should().BeTrue();
        RunAfter(AnswerWorkflow.Map, AnswerWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void TmsCall_IsOnlyMadeForAnAnswerTheMapCouldUse()
    {
        var condition = AnswerWorkflow.Action("Record_the_answer_if_it_can_be_used");

        condition["expression"]!["and"]![0]!["equals"]![0]!.GetValue<string>().Should().Be("@contains(json(body('Map_the_answer')), 'tmsRequest')");
        condition["actions"]!.AsObject().Should().ContainKey(AnswerWorkflow.TmsCall);
        condition["else"]!["actions"]!.AsObject().Should().BeEmpty();
    }

    [Fact]
    public void TmsCall_PostsTheMappedAnswerToTheBookingsAnswerOperationWithTheCorrelationId()
    {
        var inputs = AnswerWorkflow.Action(AnswerWorkflow.TmsCall)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('tmsBaseUrl')}/v1/carrier-bookings/@{json(body('Map_the_answer'))?['carrierBookingNumber']}/answer");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_answer'))?['tmsRequest']");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
    }

    [Fact]
    public void TmsCall_AuthenticatesAsTheAnswerIdentityForTheTms()
    {
        JsonNode.DeepEquals(AnswerWorkflow.Action(AnswerWorkflow.TmsCall)["inputs"]!["authentication"], new JsonObject
        {
            ["type"] = "ManagedServiceIdentity",
            ["identity"] = "@parameters('answerIdentityId')",
            ["audience"] = "@parameters('tmsAudience')",
        }).Should().BeTrue();
    }

    [Fact]
    public void TmsCall_RetryPolicy_IsExponentialAndBounded_AndFitsInTheTimeTheCarrierWaits()
    {
        var retryPolicy = AnswerWorkflow.Action(AnswerWorkflow.TmsCall)["inputs"]!["retryPolicy"]!;
        var retries = retryPolicy["count"]!.GetValue<int>();
        var longestWait = System.Xml.XmlConvert.ToTimeSpan(retryPolicy["maximumInterval"]!.GetValue<string>());
        var attemptTimeout = TimeSpan.Parse(
            AnswerWorkflow.HostFile["extensions"]!["workflow"]!["settings"]!["Runtime.Backend.HttpOperation.RequestTimeout"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture);

        retryPolicy["type"]!.GetValue<string>().Should().Be("exponential");
        retries.Should().BeInRange(1, 4);
        ((retries + 1) * attemptTimeout + retries * longestWait).Should().BeLessThan(TimeSpan.FromSeconds(200));
    }

    [Fact]
    public void TmsCall_TrackedProperties_AreTheKeysOfTheTrackingContract_ReadingOnlyTheCallItself()
    {
        var tracked = AnswerWorkflow.Action(AnswerWorkflow.TmsCall)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo(
            "correlationId", "flow", "partyId", "carrierBookingNumber", "shipmentNumber", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(AnswerWorkflow.Flow);
        foreach (var (key, value) in tracked.Where(property => property.Key != "flow"))
        {
            // Logic Apps lets a tracked property read only its own action's inputs and outputs.
            value!.GetValue<string>().Should().NotContainAny(["trigger", "body(", "outputs(", "actions("], $"{key} may read only action()");
        }
    }

    [Theory]
    [InlineData("BookingNotOpen")]
    [InlineData("BookingNotFound")]
    [InlineData("CarrierNotAllowed")]
    [InlineData("TmsCallRefused")]
    [InlineData("TmsUnavailable")]
    public void TmsCall_FailureCodes_AreTheStableOnes(string failureCode)
    {
        AnswerWorkflow.Action(AnswerWorkflow.TmsCall)["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain($"'{failureCode}'");
    }

    [Fact]
    public void RelayTheOutcome_RunsWhateverTheTmsCallEndedIn_AndCallsTheRelayWithWhatTheTmsAnswered()
    {
        var relay = AnswerWorkflow.Action(AnswerWorkflow.Relay);

        RunAfter(AnswerWorkflow.Relay, "Record_the_answer_if_it_can_be_used").Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut", "Skipped");
        relay["inputs"]!["functionName"]!.GetValue<string>().Should().Be("RelayTmsAnswer");
        JsonNode.DeepEquals(relay["inputs"]!["parameters"], new JsonObject
        {
            ["mappedAnswer"] = "@{body('Map_the_answer')}",
            ["tmsStatusCode"] = "@coalesce(actions('Answer_to_the_TMS')?['outputs']?['statusCode'], 0)",
            ["tmsResponse"] = "@{coalesce(actions('Answer_to_the_TMS')?['outputs']?['body'], '')}",
            ["correlationId"] = "@{triggerOutputs()?['headers']?['X-Correlation-ID']}",
        }).Should().BeTrue();
    }

    [Fact]
    public void AnswerTheCarrier_RespondsWithTheRelayedStatusAndBody()
    {
        var response = AnswerWorkflow.Action("Answer_the_Carrier");

        response["type"]!.GetValue<string>().Should().Be("Response");
        response["inputs"]!["statusCode"]!.GetValue<string>().Should().Be("@json(body('Relay_the_TMS_outcome'))?['statusCode']");
        response["inputs"]!["body"]!.GetValue<string>().Should().Be("@json(body('Relay_the_TMS_outcome'))?['body']");
        response["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
    }

    [Fact]
    public void EndTheRun_FailsTheRunWithTheRelaysFailureCode_OnlyWhenThereIsOne()
    {
        // A refusal the TMS gave, such as BookingNotOpen, is a business outcome the Carrier has been told; the run is fine.
        var end = AnswerWorkflow.Action("End_the_run");
        var fail = end["actions"]!.AsObject().Single().Value!;

        end["expression"]!.ToJsonString().Should().Contain("failureCode");
        fail["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
        fail["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("@{json(body('Relay_the_TMS_outcome'))?['failureCode']}");
        end["else"]!["actions"]!.AsObject().Single().Value!["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Succeeded");
    }

    [Fact]
    public void Host_SendsEnhancedTelemetry_AndSamplesNothingATrackedRecordNeeds()
    {
        AnswerWorkflow.HostFile["extensions"]!["workflow"]!["settings"]!["Runtime.ApplicationInsightTelemetryVersion"]!.GetValue<string>().Should().Be("v2");
        AnswerWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>()
            .Should().Contain("Request").And.Contain("Exception");
    }

    [Fact]
    public void Parameters_ComeFromAppSettings()
    {
        var parameters = AnswerWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "answerIdentityId", "apiManagementPrincipalId");
        foreach (var (name, parameter) in parameters)
        {
            parameter!["value"]!.GetValue<string>().Should().StartWith("@appsetting('", $"{name} comes from an app setting");
        }
    }

    [Fact]
    public void Workflow_IsStateful_AndUsesNoSecureInputsOrOutputs()
    {
        AnswerWorkflow.Kind.Should().Be("Stateful");
        foreach (var (name, operation) in AnswerWorkflow.Operations())
        {
            operation["runtimeConfiguration"]?["secureData"].Should().BeNull($"{name} should show its inputs and outputs");
        }
    }
}
