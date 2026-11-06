using Norvia.Agents.Runner;

namespace Norvia.Tms.Api.IntegrationTests.CustomerServiceAgent;

/// <summary>
/// The Agent's tool calls on the Rejected booking's Message trail, and what a failing one gives. The Agent runs in the test as the TMS UI runs it in process, with the
/// scripted model, against the stand-in, which calls the TMS API's agent view. The trail is the logs the stand-in and the TMS API wrote under the booking's correlation ID.
/// The TMS UI shows "Assistant unavailable" for any result that is not a success, whatever its code (ExplainRejectedBooking.razor, covered by its component tests).
/// </summary>
public sealed class AgentTrailTests(CustomerServiceAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExplainingABooking_PutsTheToolCallOnTheTrail_TaggedWithTheAgentAndTheTool()
    {
        var agentId = CustomerServiceAgentFixture.NewAgentId();
        var booking = await CustomerServiceAgentCalls.RejectAsync(fixture, _cancellationToken);

        var run = await CustomerServiceAgentCalls.ExplainAsync(fixture, agentId, booking.Id, booking.CorrelationId, _cancellationToken);

        run.Suggestion.Evidence.Select(evidence => evidence.ToolName).Should().Contain(CustomerServiceAgentCalls.Tool, "the answer cites what the tool returned");
        var standIn = await fixture.StandInLogAsync(log => IsHop(log, CustomerServiceAgentCalls.StandInFlow, booking.CorrelationId, agentId, "Succeeded"), _cancellationToken);
        var agentView = await fixture.TmsLogAsync(log => IsHop(log, CustomerServiceAgentCalls.AgentViewFlow, booking.CorrelationId, agentId, "Succeeded"), _cancellationToken);
        standIn["toolName"].Should().Be(CustomerServiceAgentCalls.Tool);
        agentView["toolName"].Should().Be(CustomerServiceAgentCalls.Tool);
    }

    [Fact]
    public async Task AToolCallTheAgentViewRefuses_IsAgentToolRefused_AndIsRefusedOnTheTrail()
    {
        var agentId = CustomerServiceAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();
        var noSuchBooking = Guid.NewGuid();

        var run = () => CustomerServiceAgentCalls.ExplainAsync(fixture, agentId, noSuchBooking, correlationId, _cancellationToken);

        // The TMS UI turns this into Result.Unavailable("AgentToolRefused") and shows "Assistant unavailable" (LocalAgentClient).
        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be("AgentToolRefused");
        var standIn = await fixture.StandInLogAsync(log => IsHop(log, CustomerServiceAgentCalls.StandInFlow, correlationId, agentId, "Refused"), _cancellationToken);
        standIn["failureCode"].Should().Be("AgentToolRefused");
        standIn["toolName"].Should().Be(CustomerServiceAgentCalls.Tool);
        await fixture.TmsLogAsync(log => IsHop(log, CustomerServiceAgentCalls.AgentViewFlow, correlationId, agentId, "Refused"), _cancellationToken);
    }

    private static bool IsHop(TmsLog log, string flow, string correlationId, string agentId, string outcome) =>
        log["flow"] == flow && log["correlationId"] == correlationId && log["agentId"] == agentId && log["toolName"] == CustomerServiceAgentCalls.Tool && log["outcome"] == outcome;
}
