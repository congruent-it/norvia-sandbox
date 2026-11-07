using Norvia.Agents.Runner;

namespace Norvia.Erp.Api.IntegrationTests.FinanceAgent;

/// <summary>
/// The Agent's tool calls on the Carrier invoice's Message trail, and what a failing one gives. The Agent runs in the test as the ERP UI runs it in process, with the scripted model,
/// against the stand-in, which calls the ERP API's agent view. The trail is the logs the stand-in and the ERP API wrote under the invoice's correlation ID.
/// The ERP UI shows "Assistant unavailable" for any result that is not a success, whatever its code (ExplainInvoiceLine.razor, covered by its component tests).
/// </summary>
public sealed class AgentTrailTests(FinanceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExplainingALine_PutsTheToolCallOnTheTrail_TaggedWithTheAgentAndTheTool()
    {
        var agentId = FinanceAgentFixture.NewAgentId();
        var (_, reference) = await FinanceAgentCalls.RecordCostAsync(fixture, 80m, _cancellationToken);
        var invoiceNumber = FinanceAgentCalls.NewInvoiceNumber();
        var received = await FinanceAgentCalls.ReceiveAsync(fixture, invoiceNumber, _cancellationToken, lines: (reference, 95m));
        var correlationId = received["correlationId"]!.GetValue<string>();

        var run = await FinanceAgentCalls.ExplainAsync(fixture, agentId, FinanceAgentCalls.Carrier, invoiceNumber, reference, correlationId, _cancellationToken);

        run.Suggestion.Evidence.Select(evidence => evidence.ToolName).Should().Contain(FinanceAgentCalls.Tool, "the answer cites what the tool returned");
        var standIn = await fixture.StandInLogAsync(log => IsHop(log, FinanceAgentCalls.StandInFlow, correlationId, agentId, "Succeeded"), _cancellationToken);
        var agentView = await fixture.ErpLogAsync(log => IsHop(log, FinanceAgentCalls.AgentViewFlow, correlationId, agentId, "Succeeded"), _cancellationToken);
        standIn["toolName"].Should().Be(FinanceAgentCalls.Tool);
        agentView["toolName"].Should().Be(FinanceAgentCalls.Tool);
    }

    [Fact]
    public async Task AToolCallTheAgentViewRefuses_IsAgentToolRefused_AndIsRefusedOnTheTrail()
    {
        var agentId = FinanceAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();

        var run = () => FinanceAgentCalls.ExplainAsync(
            fixture, agentId, FinanceAgentCalls.Carrier, FinanceAgentCalls.NewInvoiceNumber(), FinanceAgentCalls.NewReference(), correlationId, _cancellationToken);

        // The ERP UI turns this into Result.Unavailable("AgentToolRefused") and shows "Assistant unavailable" (LocalAgentClient).
        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be("AgentToolRefused");
        var standIn = await fixture.StandInLogAsync(log => IsHop(log, FinanceAgentCalls.StandInFlow, correlationId, agentId, "Refused"), _cancellationToken);
        standIn["failureCode"].Should().Be("AgentToolRefused");
        standIn["toolName"].Should().Be(FinanceAgentCalls.Tool);
        await fixture.ErpLogAsync(log => IsHop(log, FinanceAgentCalls.AgentViewFlow, correlationId, agentId, "Refused"), _cancellationToken);
    }

    private static bool IsHop(TrailLog log, string flow, string correlationId, string agentId, string outcome) =>
        log["flow"] == flow && log["correlationId"] == correlationId && log["agentId"] == agentId && log["toolName"] == FinanceAgentCalls.Tool && log["outcome"] == outcome;
}
