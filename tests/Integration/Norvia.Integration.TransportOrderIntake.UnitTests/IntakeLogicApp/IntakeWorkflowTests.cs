using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests.IntakeLogicApp;

/// <summary>
/// The intake Logic App's workflow definition keeps to the tracking contract (coding guidelines §7) and P1. The
/// Logic Apps runtime runs only in Azure, so these tests read the definition; the map and the relay it calls are
/// tested on their own.
/// </summary>
public sealed class IntakeWorkflowTests
{
    private const string Flow = "transport-order-intake";

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCorrelationId()
    {
        var clientTrackingId = IntakeWorkflow.Trigger["correlation"]?["clientTrackingId"]?.GetValue<string>();

        clientTrackingId.Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
    }

    [Fact]
    public void Trigger_TrackedProperties_AreTheKeysItKnows()
    {
        var tracked = IntakeWorkflow.Trigger["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "partyId");
        tracked["correlationId"]!.GetValue<string>().Should().Be("@trigger()?['outputs']?['headers']?['X-Correlation-ID']");
        tracked["flow"]!.GetValue<string>().Should().Be(Flow);
        tracked["partyId"]!.GetValue<string>().Should().Be("@trigger()?['outputs']?['headers']?['X-Shipper-ID']");
    }

    [Fact]
    public void CallerCheck_RefusesACallerOtherThanApiManagementBeforeAnythingElse()
    {
        // Easy Auth lets only API Management's identity through, but a SAS-signed callback URL skips it; then Easy Auth
        // sets no client principal (App Service strips one a caller sends).
        var check = IntakeWorkflow.Action(IntakeWorkflow.CallerCheck);

        check["type"]!.GetValue<string>().Should().Be("If");
        check["runAfter"]!.AsObject().Should().BeEmpty();
        JsonNode.DeepEquals(check["expression"], new JsonObject
        {
            ["and"] = new JsonArray(new JsonObject
            {
                ["equals"] = new JsonArray(
                    "@triggerOutputs()?['headers']?['X-MS-CLIENT-PRINCIPAL-ID']",
                    "@parameters('apiManagementPrincipalId')"),
            }),
        }).Should().BeTrue();
        IntakeWorkflow.Action("Map_the_booking")["runAfter"]![IntakeWorkflow.CallerCheck]!.AsArray()
            .Select(status => status!.GetValue<string>()).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void CallerCheck_AnswersAnotherCaller403AndFailsTheRunAsRefused()
    {
        var refusal = IntakeWorkflow.Action(IntakeWorkflow.CallerCheck)["else"]!["actions"]!.AsObject();
        var answer = refusal.Select(action => action.Value!).Single(action => action["type"]!.GetValue<string>() == "Response");
        var end = refusal.Select(action => action.Value!).Single(action => action["type"]!.GetValue<string>() == "Terminate");

        IntakeWorkflow.Action(IntakeWorkflow.CallerCheck)["actions"]!.AsObject().Should().BeEmpty();
        answer["inputs"]!["statusCode"]!.GetValue<int>().Should().Be(403);
        end["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
        end["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("CallerNotApiManagement");
    }

    [Fact]
    public void CallerCheck_TracksTheRefusalWithTheKeysOfTheTrackingContract()
    {
        var answer = IntakeWorkflow.Action("Refuse_the_caller");
        var tracked = answer["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
        tracked["correlationId"]!.GetValue<string>().Should().Be("@action()?['inputs']?['headers']?['X-Correlation-ID']");
        tracked["flow"]!.GetValue<string>().Should().Be(Flow);
        tracked["outcome"]!.GetValue<string>().Should().Be("Refused");
        tracked["failureCode"]!.GetValue<string>().Should().Be("CallerNotApiManagement");
    }

    [Fact]
    public void MapTheBooking_CallsTheIntakeMapWithTheBookingAndTheShipperFromTheHeader()
    {
        var map = IntakeWorkflow.Action("Map_the_booking");

        map["type"]!.GetValue<string>().Should().Be("InvokeFunction");
        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapPartnerBooking");
        JsonNode.DeepEquals(map["inputs"]!["parameters"], new JsonObject
        {
            ["partnerBooking"] = "@{base64(triggerBody())}",
            ["shipperId"] = "@{triggerOutputs()?['headers']?['X-Shipper-ID']}",
        }).Should().BeTrue();
    }

    [Fact]
    public void LocalFunctions_GetTheBookingEncoded_SoItsFieldsCantStandInForTheirParameters()
    {
        // The Functions runtime reads the fields of a parameter that holds JSON text as parameters too: a booking with
        // a shipperId field would then choose the Shipper (ADR 0005). Base64 text has no fields.
        foreach (var (name, operation) in IntakeWorkflow.Operations().Where(operation => operation.Operation["type"]?.GetValue<string>() == "InvokeFunction"))
        {
            operation["inputs"]!["parameters"]!["partnerBooking"]!.GetValue<string>().Should().Be("@{base64(triggerBody())}", name);
        }
    }

    [Fact]
    public void TmsCall_PostsTheMappedTransportOrderWithTheCorrelationId()
    {
        var inputs = IntakeWorkflow.Action(IntakeWorkflow.TmsCall)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('tmsBaseUrl')}/v1/transport-orders");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_booking'))");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
    }

    [Fact]
    public void TmsCall_AuthenticatesAsTheIntakeIdentityForTheTms()
    {
        var authentication = IntakeWorkflow.Action(IntakeWorkflow.TmsCall)["inputs"]!["authentication"];

        JsonNode.DeepEquals(authentication, new JsonObject
        {
            ["type"] = "ManagedServiceIdentity",
            ["identity"] = "@parameters('intakeIdentityId')",
            ["audience"] = "@parameters('tmsAudience')",
        }).Should().BeTrue();
    }

    [Fact]
    public void TmsCall_RetryPolicy_IsExponentialAndBounded()
    {
        var retryPolicy = IntakeWorkflow.Action(IntakeWorkflow.TmsCall)["inputs"]!["retryPolicy"]!;

        retryPolicy["type"]!.GetValue<string>().Should().Be("exponential");
        retryPolicy["count"]!.GetValue<int>().Should().BeInRange(1, 4);
        retryPolicy["maximumInterval"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TmsCall_TrackedProperties_AreTheKeysOfTheTrackingContract()
    {
        var tracked = IntakeWorkflow.Action(IntakeWorkflow.TmsCall)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo(
            "correlationId", "flow", "partyId", "transportOrderNumber", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(Flow);
    }

    [Fact]
    public void TmsCall_TrackedProperties_ReferenceOnlyTheCallItself()
    {
        // Logic Apps lets a tracked property read only its own action's inputs and outputs.
        var tracked = IntakeWorkflow.Action(IntakeWorkflow.TmsCall)["trackedProperties"]!.AsObject();

        foreach (var (key, value) in tracked.Where(property => property.Key != "flow"))
        {
            var expression = value!.GetValue<string>();
            expression.Should().StartWith("@", $"{key} is read from the call");
            expression.Should().NotContainAny(["trigger", "body(", "outputs(", "actions("], $"{key} may read only action()");
        }
    }

    [Fact]
    public void RelayTheOutcome_RunsWhateverTheTmsCallEndedIn()
    {
        var relay = IntakeWorkflow.Action("Relay_the_TMS_outcome");

        relay["runAfter"]![IntakeWorkflow.TmsCall]!.AsArray().Select(status => status!.GetValue<string>())
            .Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut", "Skipped");
    }

    [Fact]
    public void RelayTheOutcome_CallsTheRelayWithWhatTheTmsAnswered()
    {
        var relay = IntakeWorkflow.Action("Relay_the_TMS_outcome");

        relay["inputs"]!["functionName"]!.GetValue<string>().Should().Be("RelayTmsOutcome");
        JsonNode.DeepEquals(relay["inputs"]!["parameters"], new JsonObject
        {
            ["tmsStatusCode"] = "@coalesce(actions('Submit_to_the_TMS')?['outputs']?['statusCode'], 0)",
            ["tmsResponse"] = "@{coalesce(actions('Submit_to_the_TMS')?['outputs']?['body'], '')}",
            ["correlationId"] = "@{triggerOutputs()?['headers']?['X-Correlation-ID']}",
            ["partnerBooking"] = "@{base64(triggerBody())}",
        }).Should().BeTrue();
    }

    [Fact]
    public void AnswerTheShipper_RespondsWithTheRelayedStatusAndBody()
    {
        var response = IntakeWorkflow.Action("Answer_the_Shipper");

        response["type"]!.GetValue<string>().Should().Be("Response");
        response["inputs"]!["statusCode"]!.GetValue<string>().Should().Be("@json(body('Relay_the_TMS_outcome'))?['statusCode']");
        response["inputs"]!["body"]!.GetValue<string>().Should().Be("@json(body('Relay_the_TMS_outcome'))?['body']");
        response["inputs"]!["headers"]!["Content-Type"]!.GetValue<string>().Should().Be("application/json");
    }

    [Fact]
    public void EndTheRun_FailsTheRunOnlyWhenTheBookingWasNotReceived()
    {
        var end = IntakeWorkflow.Action("End_the_run");

        end["type"]!.GetValue<string>().Should().Be("If");
        end["expression"]!.ToJsonString().Should().Contain("NOT_RECEIVED");
        end["actions"]!.AsObject().Single().Value!["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
        end["else"]!["actions"]!.AsObject().Single().Value!["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Succeeded");
    }

    [Fact]
    public void TmsCall_EveryAttemptAndItsWaits_FitInTheTimeTheShipperWaits()
    {
        // The Shipper's call waits for the answer, and Azure closes a request that takes longer than 230 seconds. So
        // "not received" must come back before then: every attempt timing out, with the longest wait between them.
        var retryPolicy = IntakeWorkflow.Action(IntakeWorkflow.TmsCall)["inputs"]!["retryPolicy"]!;
        var retries = retryPolicy["count"]!.GetValue<int>();
        var longestWait = System.Xml.XmlConvert.ToTimeSpan(retryPolicy["maximumInterval"]!.GetValue<string>());
        var attemptTimeout = TimeSpan.Parse(HostSetting("Runtime.Backend.HttpOperation.RequestTimeout"), System.Globalization.CultureInfo.InvariantCulture);

        var worstCase = (retries + 1) * attemptTimeout + retries * longestWait;

        worstCase.Should().BeLessThan(TimeSpan.FromSeconds(200), "it leaves time to map, relay and answer");
    }

    [Fact]
    public void Host_SendsEnhancedTelemetry()
    {
        // Enhanced telemetry puts the tracked properties and the client tracking ID on each run's records in
        // Application Insights, where the Integration console finds the run by correlation ID.
        HostSetting("Runtime.ApplicationInsightTelemetryVersion").Should().Be("v2");
    }

    [Fact]
    public void Parameters_ComeFromAppSettings()
    {
        // The estate sets them for the deployed Logic App; nothing environment-specific is in source.
        var parameters = IntakeWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("tmsBaseUrl", "tmsAudience", "intakeIdentityId", "apiManagementPrincipalId");
        foreach (var (name, parameter) in parameters)
        {
            parameter!["value"]!.GetValue<string>().Should().StartWith("@appsetting('", $"{name} comes from an app setting");
        }
    }

    [Fact]
    public void EndTheRun_FailsWithTheRelaysFailureCode()
    {
        // The relay decides why the booking wasn't received, so the run's error says the same as the Shipper's answer.
        var fail = IntakeWorkflow.Action("End_the_run")["actions"]!.AsObject().Single().Value!;

        fail["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("@{json(body('Relay_the_TMS_outcome'))?['failureCode']}");
    }

    [Fact]
    public void Operations_UseNoSecureInputsOrOutputs()
    {
        // Secure inputs and outputs would hide the bodies the Integration operator needs; no step here handles secrets.
        foreach (var (name, operation) in IntakeWorkflow.Operations())
        {
            operation["runtimeConfiguration"]?["secureData"].Should().BeNull($"{name} should show its inputs and outputs");
        }
    }

    private static string HostSetting(string name) =>
        IntakeWorkflow.HostFile["extensions"]?["workflow"]?["settings"]?[name]?.GetValue<string>()
        ?? throw new InvalidOperationException($"host.json doesn't set {name}.");
}
