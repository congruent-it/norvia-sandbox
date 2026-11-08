using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// A rejection whose reason the Agent drafted traces the Agent and its run ID, edited or not (#473); one Finance wrote themselves traces none, and a draft nobody confirms writes nothing.
/// The draft is the real run of the rejection reason agent's definition against the stand-in. Each test records the Carrier costs and Carrier invoices it needs under numbers of its own.
/// </summary>
public sealed class RejectionReasonTraceTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARejectionWhoseReasonTheAgentDrafted_TracesTheAgentAndTheRunId_EditedOrNot(bool edited)
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, correlationId) = await ReceiveAsync(reference, 95m);
        var agentId = FinanceAgentFixture.NewAgentId();
        var run = await RejectionReasonCalls.DraftAsync(fixture, agentId, FinanceAgentCalls.Carrier, invoiceNumber, reference, correlationId, _cancellationToken);
        run.Suggestion.Cause.Should().Contain("€80.00", "the draft was composed from the line's facts");
        var reason = edited ? run.Suggestion.Cause + " Thank you for your understanding." : run.Suggestion.Cause;

        using var response = await FinanceAgentCalls.DecideAsync(
            fixture, invoiceNumber, reference, "reject", new JsonObject { ["reason"] = reason, ["agentName"] = agentId, ["agentRunId"] = run.RunId }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_cancellationToken));
        var trace = await fixture.ErpLogAsync(log => IsRejection(log, correlationId) && log["agentRunId"] == run.RunId, _cancellationToken);
        trace["agentId"].Should().Be(agentId);
        trace["documentNumber"].Should().Be(invoiceNumber);
    }

    [Fact]
    public async Task ARejectionWithAReasonFinanceWrote_TracesNoAgentAndNoRun()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, correlationId) = await ReceiveAsync(reference, 95m);

        using var response = await FinanceAgentCalls.DecideAsync(
            fixture, invoiceNumber, reference, "reject", new JsonObject { ["reason"] = "Not what we agreed." }, _cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_cancellationToken));
        await fixture.ErpLogAsync(log => IsRejection(log, correlationId), _cancellationToken);
        var decisions = fixture.ErpLogs.Where(log => IsRejection(log, correlationId)).ToList();
        decisions.Should().NotBeEmpty("the rejection was traced, so the check below is not vacuous");
        decisions.Should().OnlyContain(log => !log.Scope.ContainsKey("agentId") && !log.Scope.ContainsKey("agentRunId"));
    }

    [Fact]
    public async Task ADraftNobodyConfirms_WritesNothing_TheLineStaysToDecide()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, correlationId) = await ReceiveAsync(reference, 95m);

        var run = await RejectionReasonCalls.DraftAsync(fixture, FinanceAgentFixture.NewAgentId(), FinanceAgentCalls.Carrier, invoiceNumber, reference, correlationId, _cancellationToken);

        run.Suggestion.Cause.Should().NotBeNullOrWhiteSpace();
        var view = await FinanceAgentCalls.ReadLineAsync(fixture, invoiceNumber, reference, correlationId, _cancellationToken);
        view["status"]!.GetValue<string>().Should().Be("ToDecide");
        fixture.ErpLogs.Where(log => IsRejection(log, correlationId)).Should().BeEmpty("no decision was taken");
    }

    [Fact]
    public async Task TheDraft_ForALineWithASpotRemark_CarriesNeitherTheRemarkNorTheRateCard()
    {
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var (invoiceNumber, correlationId) = await ReceiveAsync(reference, 95m);

        var run = await RejectionReasonCalls.DraftAsync(fixture, FinanceAgentFixture.NewAgentId(), FinanceAgentCalls.Carrier, invoiceNumber, reference, correlationId, _cancellationToken);

        var everything = run.Suggestion.Cause + run.Suggestion.SuggestedAction + string.Join(' ', run.Suggestion.Evidence.Select(evidence => evidence.Claim));
        everything.Should().NotContainAny(FinanceAgentCalls.NeverForTheAgent).And.NotContainEquivalentOf("Spot").And.NotContainEquivalentOf("rate card");
        run.ToolCalls.Select(call => call.ToolName).Should().Equal(RejectionReasonCalls.Tool);
    }

    private async Task<(string InvoiceNumber, string CorrelationId)> ReceiveAsync(string reference, decimal amount)
    {
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, amount));
        return (invoiceNumber, received["correlationId"]!.GetValue<string>());
    }

    // A log of the decision handlers: on the Carrier invoice's own trail, and not the agent view's reads.
    private static bool IsRejection(TrailLog log, string correlationId) =>
        log["correlationId"] == correlationId && log["flow"] == "carrier-invoices" && log.Text.Contains("Rejected ", StringComparison.Ordinal);
}
