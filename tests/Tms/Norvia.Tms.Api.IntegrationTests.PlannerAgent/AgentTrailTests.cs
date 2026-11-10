using Norvia.Agents.Runner;

namespace Norvia.Tms.Api.IntegrationTests.PlannerAgent;

/// <summary>
/// The Agent's tool call on the Transport Order's Message trail, and what a failing one gives. The Agent runs in the test as the TMS UI runs it in process, with the
/// scripted model, against the stand-in, which calls the TMS API's agent view. The trail is the logs the stand-in and the TMS API wrote under the correlation ID of the question.
/// The TMS UI shows "Assistant unavailable" for any result that is not a success, whatever its code (SuggestAssignment.razor, covered by its component tests).
/// </summary>
public sealed class AgentTrailTests(PlannerAgentFixture fixture)
{
    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuggestingAnAssignment_PutsTheToolCallOnTheTrail_TaggedWithTheAgentAndTheTool()
    {
        var agentId = PlannerAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();
        var number = await PlannerAgentCalls.AcceptedAsync(fixture, PlannerAgentCalls.BookingOn(PlannerAgentCalls.Weekday(0)), _cancellationToken);

        var run = await PlannerAgentCalls.SuggestAsync(fixture, agentId, number, correlationId, _cancellationToken);

        run.Suggestion.Evidence.Select(evidence => evidence.ToolName).Should().Contain(PlannerAgentCalls.Tool, "the answer cites what the tool returned");
        var standIn = await fixture.StandInLogAsync(log => IsHop(log, PlannerAgentCalls.StandInFlow, correlationId, agentId, "Succeeded"), _cancellationToken);
        var agentView = await fixture.TmsLogAsync(log => IsHop(log, PlannerAgentCalls.AgentViewFlow, correlationId, agentId, "Succeeded"), _cancellationToken);
        standIn["toolName"].Should().Be(PlannerAgentCalls.Tool);
        agentView["toolName"].Should().Be(PlannerAgentCalls.Tool);
    }

    [Fact]
    public async Task AToolCallTheAgentViewRefuses_IsAgentToolRefused_AndIsRefusedOnTheTrail()
    {
        var agentId = PlannerAgentFixture.NewAgentId();
        var correlationId = Guid.NewGuid().ToString();

        var run = () => PlannerAgentCalls.SuggestAsync(fixture, agentId, "TO-9999999", correlationId, _cancellationToken);

        // The TMS UI turns this into Result.Unavailable("AgentToolRefused") and shows "Assistant unavailable" (LocalAgentClient).
        (await run.Should().ThrowAsync<AgentRunException>()).Which.FailureCode.Should().Be("AgentToolRefused");
        var standIn = await fixture.StandInLogAsync(log => IsHop(log, PlannerAgentCalls.StandInFlow, correlationId, agentId, "Refused"), _cancellationToken);
        standIn["failureCode"].Should().Be("AgentToolRefused");
        standIn["toolName"].Should().Be(PlannerAgentCalls.Tool);
        await fixture.TmsLogAsync(log => IsHop(log, PlannerAgentCalls.AgentViewFlow, correlationId, agentId, "Refused"), _cancellationToken);
    }

    private static bool IsHop(TmsLog log, string flow, string correlationId, string agentId, string outcome) =>
        log["flow"] == flow && log["correlationId"] == correlationId && log["agentId"] == agentId && log["toolName"] == PlannerAgentCalls.Tool && log["outcome"] == outcome;
}
