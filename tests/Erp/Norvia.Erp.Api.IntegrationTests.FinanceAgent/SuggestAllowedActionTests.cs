using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// What the ERP lets a Suggestion name (E2): the allowed actions the agent view carries equal the rules Accept and Reject enforce, a decision made from a Suggestion traces the Agent and its
/// run ID while one made by hand traces none, and a Suggestion nobody acts on writes nothing. Each test records the Carrier costs and Carrier invoices it needs under numbers of its own.
/// </summary>
public sealed class SuggestAllowedActionTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALineWithAnOpenCarrierCostOfItsCarrier_AllowsAccept_AcceptAdjustedAndReject_ButNotWait()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 95m);

        var view = await FinanceAgentCalls.ReadLineAsync(fixture, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        view["result"]!.GetValue<string>().Should().Be("PriceMismatch");
        ActionsOf(view).Should().BeEquivalentTo("accept", "accept-adjusted", "reject");
    }

    [Fact]
    public async Task AnUnknownBookingLine_NeverAllowsAccept_OnlyRejectOrWait()
    {
        var reference = FinanceAgentCalls.NewReference();
        var (invoiceNumber, received) = await ReceiveAsync(reference, 4_321.09m);

        var view = await FinanceAgentCalls.ReadLineAsync(fixture, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        view["result"]!.GetValue<string>().Should().Be("UnknownBooking");
        ActionsOf(view).Should().BeEquivalentTo("reject", "wait");
    }

    [Fact]
    public async Task AnUnknownBookingLine_WhoseReferenceOnlyAnotherCarrierHasAnOpenCostFor_StillNeverAllowsAccept()
    {
        var reference = FinanceAgentCalls.NewReference();
        await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken, reference, FinanceAgentCalls.OtherCarrier);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 100m);

        var view = await FinanceAgentCalls.ReadLineAsync(fixture, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        view["result"]!.GetValue<string>().Should().Be("UnknownBooking", "the other Carrier's cost is not this Carrier's");
        ActionsOf(view).Should().NotContain(["accept", "accept-adjusted"]);
    }

    [Fact]
    public async Task ADuplicateLine_NeverAllowsAccept_AndDoesNotWait()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken);
        await ReceiveAsync(reference, 100m);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 100m);

        var view = await FinanceAgentCalls.ReadLineAsync(fixture, invoiceNumber, reference, CorrelationOf(received), _cancellationToken);

        view["result"]!.GetValue<string>().Should().Be("Duplicate");
        view["carrierCost"]!["status"]!.GetValue<string>().Should().Be("Invoiced", "a cost that is not Open can't be accepted");
        ActionsOf(view).Should().BeEquivalentTo("reject");
    }

    [Fact]
    public async Task AcceptingAnUnknownBookingLine_IsRefused_ThoughTheAgentNeverOffersIt()
    {
        var reference = FinanceAgentCalls.NewReference();
        var (invoiceNumber, _) = await ReceiveAsync(reference, 50m);

        using var response = await FinanceAgentCalls.DecideAsync(fixture, invoiceNumber, reference, "accept", null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the ERP enforces the rule the allowed actions state");
    }

    [Fact]
    public async Task AcceptingADuplicateLine_IsRefused_ThoughTheAgentNeverOffersIt()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 100m, _cancellationToken);
        await ReceiveAsync(reference, 100m);
        var (invoiceNumber, _) = await ReceiveAsync(reference, 100m);

        using var response = await FinanceAgentCalls.DecideAsync(fixture, invoiceNumber, reference, "accept", null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the Carrier cost is already Invoiced");
        using var adjusted = await FinanceAgentCalls.DecideAsync(
            fixture, invoiceNumber, reference, "accept", new JsonObject { ["adjustedAmount"] = 90m, ["remark"] = "Too high." }, _cancellationToken);
        adjusted.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("accept-adjusted")]
    [InlineData("reject")]
    public async Task ADecisionMadeFromASuggestion_TracesTheAgentAndTheRunIdOfTheRunThatMadeIt(string action)
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 95m);
        var correlationId = CorrelationOf(received);
        var agentId = FinanceAgentFixture.NewAgentId();
        var run = await FinanceAgentCalls.ExplainAsync(fixture, agentId, FinanceAgentCalls.Carrier, invoiceNumber, reference, correlationId, _cancellationToken);
        var (decision, body) = action switch
        {
            "accept" => ("accept", new JsonObject { ["adjustedAmount"] = null, ["remark"] = null }),
            "accept-adjusted" => ("accept", new JsonObject { ["adjustedAmount"] = 85m, ["remark"] = "Waiting time was not agreed." }),
            _ => ("reject", new JsonObject { ["reason"] = "Not what we agreed." }),
        };
        body["agentName"] = agentId;
        body["agentRunId"] = run.RunId;

        using var response = await FinanceAgentCalls.DecideAsync(fixture, invoiceNumber, reference, decision, body, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_cancellationToken));
        var trace = await fixture.ErpLogAsync(log => IsDecision(log, correlationId) && log["agentRunId"] == run.RunId, _cancellationToken);
        trace["agentId"].Should().Be(agentId);
        trace["documentNumber"].Should().Be(invoiceNumber);
    }

    [Fact]
    public async Task ADecisionMadeByHand_TracesNoAgentAndNoRun()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 95m);
        var correlationId = CorrelationOf(received);

        using var response = await FinanceAgentCalls.DecideAsync(fixture, invoiceNumber, reference, "accept", null, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_cancellationToken));
        await fixture.ErpLogAsync(log => IsDecision(log, correlationId), _cancellationToken);
        var decisions = fixture.ErpLogs.Where(log => IsDecision(log, correlationId)).ToList();
        decisions.Should().NotBeEmpty("the decision was traced, so the check below is not vacuous");
        decisions.Should().OnlyContain(log => !log.Scope.ContainsKey("agentId") && !log.Scope.ContainsKey("agentRunId"));
    }

    [Fact]
    public async Task ASuggestionNobodyActsOn_WritesNothing_TheLineStaysToDecideAndItsCarrierCostOpen()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, received) = await ReceiveAsync(reference, 95m);
        var correlationId = CorrelationOf(received);

        var run = await FinanceAgentCalls.ExplainAsync(fixture, FinanceAgentFixture.NewAgentId(), FinanceAgentCalls.Carrier, invoiceNumber, reference, correlationId, _cancellationToken);

        run.Suggestion.Should().NotBeNull("the Agent answered");
        run.RunId.Should().NotBeNullOrEmpty();
        var view = await FinanceAgentCalls.ReadLineAsync(fixture, invoiceNumber, reference, correlationId, _cancellationToken);
        view["status"]!.GetValue<string>().Should().Be("ToDecide");
        view["carrierCost"]!["status"]!.GetValue<string>().Should().Be("Open");
        ActionsOf(view).Should().BeEquivalentTo("accept", "accept-adjusted", "reject");
        fixture.ErpLogs.Where(log => IsDecision(log, correlationId)).Should().BeEmpty("no decision was taken");
    }

    private async Task<(string InvoiceNumber, JsonObject Received)> ReceiveAsync(string reference, decimal amount)
    {
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        return (invoiceNumber, await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, amount)));
    }

    private static string CorrelationOf(JsonObject received) => received["correlationId"]!.GetValue<string>();

    private static IEnumerable<string> ActionsOf(JsonNode view) => view["allowedActions"]!.AsArray().Select(action => action!.GetValue<string>());

    // A log of the decision handlers: on the Carrier invoice's own trail, and not the agent view's reads.
    private static bool IsDecision(TrailLog log, string correlationId) =>
        log["correlationId"] == correlationId && log["flow"] == "carrier-invoices" && log.Text.Contains("Carrier invoice", StringComparison.Ordinal)
        && (log.Text.Contains("Accepted ", StringComparison.Ordinal) || log.Text.Contains("Rejected ", StringComparison.Ordinal));
}
