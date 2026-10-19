using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.CarrierInvoiceLogicApp;

/// <summary>
/// The Carrier invoice Logic App's workflow definition keeps to the tracking contract (coding guidelines §7), ADR 0005 and E2. The Logic
/// Apps runtime runs only in Azure, so these tests read the definition; the map and the relay it calls are tested on their own.
/// </summary>
public sealed class CarrierInvoiceWorkflowTests
{
    private static IEnumerable<string> RunAfter(string action, string after) =>
        CarrierInvoiceWorkflow.Action(action)["runAfter"]![after]!.AsArray().Select(status => status!.GetValue<string>());

    [Fact]
    public void Trigger_ClientTrackingId_IsTheCorrelationId()
    {
        CarrierInvoiceWorkflow.Trigger["correlation"]?["clientTrackingId"]?.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
    }

    [Fact]
    public void Trigger_TracksNothing_BecauseLogicAppsDropsARequestTriggersTrackedProperties()
    {
        CarrierInvoiceWorkflow.Trigger["trackedProperties"].Should().BeNull();
    }

    [Fact]
    public void Trigger_IsAnHttpPost_NamedAfterTheRouteTheApiManagementPolicyCalls()
    {
        CarrierInvoiceWorkflow.Definition["triggers"]!.AsObject().Single().Key.Should().Be("When_a_Carrier_invoices");
        CarrierInvoiceWorkflow.Trigger["inputs"]!["method"]!.GetValue<string>().Should().Be("POST");
    }

    [Fact]
    public void CallerCheck_RunsFirst_AndRefusesACallerOtherThanApiManagement()
    {
        var check = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.CallerCheck);

        check["runAfter"]!.AsObject().Should().BeEmpty();
        JsonNode.DeepEquals(check["expression"], new JsonObject
        {
            ["and"] = new JsonArray(new JsonObject
            {
                ["equals"] = new JsonArray("@triggerOutputs()?['headers']?['X-MS-CLIENT-PRINCIPAL-ID']", "@parameters('apiManagementPrincipalId')"),
            }),
        }).Should().BeTrue();
        CarrierInvoiceWorkflow.Actions.Where(action => action.Key != CarrierInvoiceWorkflow.CallerCheck)
            .Should().OnlyContain(action => action.Value!["runAfter"]!.AsObject().Count > 0, "only the caller check runs first");
    }

    [Fact]
    public void CallerCheck_AnswersAnotherCaller403_FailsTheRun_AndTracksTheRefusal()
    {
        var refusal = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.CallerCheck)["else"]!["actions"]!.AsObject();
        var answer = CarrierInvoiceWorkflow.Action("Refuse_the_caller");
        var end = refusal.Select(action => action.Value!).Single(action => action["type"]!.GetValue<string>() == "Terminate");
        var tracked = answer["trackedProperties"]!.AsObject();

        answer["inputs"]!["statusCode"]!.GetValue<int>().Should().Be(403);
        end["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("CallerNotApiManagement");
        tracked.Select(property => property.Key).Should().BeEquivalentTo("correlationId", "flow", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(CarrierInvoiceWorkflow.Flow);
        tracked["outcome"]!.GetValue<string>().Should().Be("Refused");
    }

    [Fact]
    public void Tracking_RunsStraightAfterTheCallerCheck_TakingTheCorrelationIdTheCarrierAndTheInvoiceNumberFromTheCall()
    {
        var tracking = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.Tracking);

        tracking["type"]!.GetValue<string>().Should().Be("Compose");
        RunAfter(CarrierInvoiceWorkflow.Tracking, CarrierInvoiceWorkflow.CallerCheck).Should().BeEquivalentTo("Succeeded");
        JsonNode.DeepEquals(tracking["inputs"], new JsonObject
        {
            ["correlationId"] = "@triggerOutputs()?['headers']?['X-Correlation-ID']",
            ["partyId"] = "@triggerOutputs()?['headers']?['X-Carrier-ID']",
            ["documentNumber"] = "@triggerBody()?['invoiceNo']",
        }).Should().BeTrue();
        tracking["trackedProperties"]!.AsObject().Select(property => property.Key)
            .Should().BeEquivalentTo("correlationId", "flow", "partyId", "documentNumber");
        tracking["trackedProperties"]!["flow"]!.GetValue<string>().Should().Be(CarrierInvoiceWorkflow.Flow);
    }

    [Fact]
    public void MapTheInvoice_GetsTheInvoiceEncoded_AndTheCarrierAndTheCorrelationIdFromTheHeadersAlone()
    {
        // The Functions runtime reads the fields of a parameter that holds JSON text as parameters too: an invoice with a
        // carrierId field would then choose the Carrier (ADR 0005). Base64 text has no fields.
        var map = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.Map);

        map["inputs"]!["functionName"]!.GetValue<string>().Should().Be("MapCarrierInvoice");
        JsonNode.DeepEquals(map["inputs"]!["parameters"], new JsonObject
        {
            ["carrierInvoice"] = "@{base64(triggerBody())}",
            ["carrierId"] = "@{triggerOutputs()?['headers']?['X-Carrier-ID']}",
            ["correlationId"] = "@{triggerOutputs()?['headers']?['X-Correlation-ID']}",
        }).Should().BeTrue();
        RunAfter(CarrierInvoiceWorkflow.Map, CarrierInvoiceWorkflow.Tracking).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void ErpCall_IsOnlyMadeForAnInvoiceTheMapCouldUse()
    {
        var condition = CarrierInvoiceWorkflow.Action("Receive_the_invoice_if_it_can_be_used");

        condition["expression"]!["and"]![0]!["equals"]![0]!.GetValue<string>().Should().Be("@contains(json(body('Map_the_invoice')), 'erpRequest')");
        condition["actions"]!.AsObject().Should().ContainKey(CarrierInvoiceWorkflow.ErpCall);
        condition["else"]!["actions"]!.AsObject().Should().BeEmpty();
    }

    [Fact]
    public void ErpCall_PostsTheMappedInvoiceToTheCarrierInvoicesOperationWithTheCorrelationId_TakingTheCarrierFromTheMapNotThePayload()
    {
        var inputs = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["inputs"]!;

        inputs["method"]!.GetValue<string>().Should().Be("POST");
        inputs["uri"]!.GetValue<string>().Should().Be("@{parameters('erpBaseUrl')}/v1/carrier-invoices");
        inputs["body"]!.GetValue<string>().Should().Be("@json(body('Map_the_invoice'))?['erpRequest']");
        inputs["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
        CarrierInvoiceWorkflow.WorkflowText.Should().NotContain("triggerBody()?['carrierId']", "the Carrier never comes from the payload (ADR 0005)");
    }

    [Fact]
    public void ErpCall_SetsTheRunsClientTrackingIdToTheCorrelationId_ThroughTheTrigger()
    {
        // The step that calls the application runs inside the run whose client tracking ID is the correlation ID (§7).
        CarrierInvoiceWorkflow.Trigger["correlation"]!["clientTrackingId"]!.GetValue<string>().Should().Contain("X-Correlation-ID");
        CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Contain("X-Correlation-ID");
    }

    [Fact]
    public void ErpCall_AuthenticatesAsTheCarrierInvoiceIdentityForTheErp()
    {
        JsonNode.DeepEquals(CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["inputs"]!["authentication"], new JsonObject
        {
            ["type"] = "ManagedServiceIdentity",
            ["identity"] = "@parameters('carrierInvoiceIdentityId')",
            ["audience"] = "@parameters('erpAudience')",
        }).Should().BeTrue();
    }

    [Fact]
    public void ErpCall_RetryPolicy_IsExponentialAndBounded_AndFitsInTheTimeTheCarrierWaits()
    {
        var retryPolicy = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["inputs"]!["retryPolicy"]!;
        var retries = retryPolicy["count"]!.GetValue<int>();
        var longestWait = System.Xml.XmlConvert.ToTimeSpan(retryPolicy["maximumInterval"]!.GetValue<string>());
        var attemptTimeout = TimeSpan.Parse(
            CarrierInvoiceWorkflow.HostFile["extensions"]!["workflow"]!["settings"]!["Runtime.Backend.HttpOperation.RequestTimeout"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture);

        retryPolicy["type"]!.GetValue<string>().Should().Be("exponential");
        retries.Should().BeInRange(1, 4);
        ((retries + 1) * attemptTimeout + retries * longestWait).Should().BeLessThan(TimeSpan.FromSeconds(200));
    }

    [Fact]
    public void ErpCall_TrackedProperties_AreTheKeysOfTheTrackingContract_ReadingOnlyTheCallItself()
    {
        var tracked = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["trackedProperties"]!.AsObject();

        tracked.Select(property => property.Key).Should().BeEquivalentTo(
            "correlationId", "flow", "partyId", "documentNumber", "outcome", "failureCode");
        tracked["flow"]!.GetValue<string>().Should().Be(CarrierInvoiceWorkflow.Flow);
        foreach (var (key, value) in tracked.Where(property => property.Key != "flow"))
        {
            // Logic Apps lets a tracked property read only its own action's inputs and outputs.
            value!.GetValue<string>().Should().NotContainAny(["trigger", "body(", "outputs(", "actions("], $"{key} may read only action()");
        }
    }

    [Theory]
    [InlineData("CarrierInvoiceRefused")]
    [InlineData("InvoiceNumberUsed")]
    [InlineData("CarrierNotFound")]
    [InlineData("ErpCallRefused")]
    [InlineData("ErpUnavailable")]
    public void ErpCall_FailureCodes_AreTheStableOnes(string failureCode)
    {
        CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["trackedProperties"]!["failureCode"]!.GetValue<string>().Should().Contain($"'{failureCode}'");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    [InlineData(422)]
    public void ErpCall_ARefusalOfTheErp_IsTrackedAsRefused_WithTheCodeTheRelayUses(int status)
    {
        var tracked = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["trackedProperties"]!;
        var (outcome, failureCode) = ErpInvoiceOutcome.Of(status);

        outcome.Should().Be(ErpInvoiceOutcome.Refused);
        tracked["outcome"]!.GetValue<string>().Should().Contain("createArray(400, 409, 422), action()?['outputs']?['statusCode']), 'Refused'");
        tracked["failureCode"]!.GetValue<string>().Should().Contain($"equals(action()?['outputs']?['statusCode'], {status}), '{failureCode}'");
    }

    [Fact]
    public void ErpCall_AReceivedInvoice_IsTrackedAsSucceeded_WithNoFailureCode()
    {
        var tracked = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.ErpCall)["trackedProperties"]!;

        tracked["outcome"]!.GetValue<string>().Should().StartWith("@if(contains(createArray(200, 201), action()?['outputs']?['statusCode']), 'Succeeded'");
        tracked["failureCode"]!.GetValue<string>().Should().StartWith("@if(contains(createArray(200, 201), action()?['outputs']?['statusCode']), null");
    }

    [Fact]
    public void RelayTheOutcome_RunsWhateverTheErpCallEndedIn_AndCallsTheRelayWithWhatTheErpAnswered()
    {
        var relay = CarrierInvoiceWorkflow.Action(CarrierInvoiceWorkflow.Relay);

        RunAfter(CarrierInvoiceWorkflow.Relay, "Receive_the_invoice_if_it_can_be_used").Should().BeEquivalentTo("Succeeded", "Failed", "TimedOut", "Skipped");
        relay["inputs"]!["functionName"]!.GetValue<string>().Should().Be("RelayErpInvoiceAnswer");
        JsonNode.DeepEquals(relay["inputs"]!["parameters"], new JsonObject
        {
            ["mappedInvoice"] = "@{body('Map_the_invoice')}",
            ["erpStatusCode"] = "@coalesce(actions('Receive_at_the_ERP')?['outputs']?['statusCode'], 0)",
            ["erpResponse"] = "@{coalesce(actions('Receive_at_the_ERP')?['outputs']?['body'], '')}",
            ["correlationId"] = "@{triggerOutputs()?['headers']?['X-Correlation-ID']}",
        }).Should().BeTrue();
    }

    [Fact]
    public void AnswerTheCarrier_RespondsWithTheRelayedStatusAndBody_StraightAway()
    {
        var response = CarrierInvoiceWorkflow.Action("Answer_the_Carrier");

        response["type"]!.GetValue<string>().Should().Be("Response");
        response["inputs"]!["statusCode"]!.GetValue<string>().Should().Be("@json(body('Relay_the_ERP_outcome'))?['statusCode']");
        response["inputs"]!["body"]!.GetValue<string>().Should().Be("@json(body('Relay_the_ERP_outcome'))?['body']");
        response["inputs"]!["headers"]!["X-Correlation-ID"]!.GetValue<string>().Should().Be("@{triggerOutputs()?['headers']?['X-Correlation-ID']}");
        RunAfter("Answer_the_Carrier", CarrierInvoiceWorkflow.Relay).Should().BeEquivalentTo("Succeeded");
    }

    [Fact]
    public void EndTheRun_FailsTheRunWithTheRelaysFailureCode_OnlyWhenThereIsOne()
    {
        // A refusal the ERP gave, such as InvoiceNumberUsed, is a business outcome the Carrier has been told; the run is fine.
        var end = CarrierInvoiceWorkflow.Action("End_the_run");
        var fail = end["actions"]!.AsObject().Single().Value!;

        end["expression"]!.ToJsonString().Should().Contain("failureCode");
        fail["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Failed");
        fail["inputs"]!["runError"]!["code"]!.GetValue<string>().Should().Be("@{json(body('Relay_the_ERP_outcome'))?['failureCode']}");
        end["else"]!["actions"]!.AsObject().Single().Value!["inputs"]!["runStatus"]!.GetValue<string>().Should().Be("Succeeded");
    }

    [Fact]
    public void Host_SendsEnhancedTelemetry_AndSamplesNothingATrackedRecordNeeds()
    {
        CarrierInvoiceWorkflow.HostFile["extensions"]!["workflow"]!["settings"]!["Runtime.ApplicationInsightTelemetryVersion"]!.GetValue<string>().Should().Be("v2");
        CarrierInvoiceWorkflow.HostFile["logging"]!["applicationInsights"]!["samplingSettings"]!["excludedTypes"]!.GetValue<string>()
            .Should().Contain("Request").And.Contain("Exception");
    }

    [Fact]
    public void Parameters_ComeFromAppSettings()
    {
        var parameters = CarrierInvoiceWorkflow.ParametersFile;

        parameters.Select(parameter => parameter.Key).Should().BeEquivalentTo("erpBaseUrl", "erpAudience", "carrierInvoiceIdentityId", "apiManagementPrincipalId");
        foreach (var (name, parameter) in parameters)
        {
            parameter!["value"]!.GetValue<string>().Should().StartWith("@appsetting('", $"{name} comes from an app setting");
        }
    }

    [Fact]
    public void Workflow_IsStateful_AndUsesNoSecureInputsOrOutputs()
    {
        CarrierInvoiceWorkflow.Kind.Should().Be("Stateful");
        foreach (var (name, operation) in CarrierInvoiceWorkflow.Operations())
        {
            operation["runtimeConfiguration"]?["secureData"].Should().BeNull($"{name} should show its inputs and outputs");
        }
    }
}
