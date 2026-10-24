using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norvia.IntegrationConsole.Application.DeadLetters;
using Norvia.IntegrationConsole.Application.Trails;
using Norvia.IntegrationConsole.Application.Trails.Open;

namespace Norvia.IntegrationConsole.Application.UnitTests.Trails;

public sealed class AgentTaggedHopTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_AToolCallAnAgentMade_IsAHopTaggedWithTheAgentAndTheTool()
    {
        var source = Substitute.For<ITrailSource>();
        source.HopsAsync(Arg.Any<TrailLookup>(), Arg.Any<CancellationToken>()).Returns(
        [
            new RecordedHop("mcp", null, "agent-mcp: POST /mcp", "agent-mcp-integration-operations", Now, null, Outcome.Succeeded, null, [], AgentId: "integration-operations-agent", ToolName: "get_message_trail"),
            new RecordedHop("tms", "mcp", "tms-api", null, Now.AddSeconds(1), null, Outcome.Succeeded, null, []),
        ]);
        var handler = new OpenTrailHandler(source, Substitute.For<IDeadLetterQueues>(), new FakeTimeProvider(Now), Options.Create(new IntegrationRetention()));

        var result = await handler.Handle(new OpenTrailQuery("c-1"), TestContext.Current.CancellationToken);

        result.Value.Hops.Select(hop => (hop.AgentId, hop.ToolName)).Should().Equal(("integration-operations-agent", "get_message_trail"), (null, null));
    }
}
