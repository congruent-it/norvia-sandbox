using Microsoft.Extensions.AI;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft;

/// <summary>The port locally: the in-process runner over a chat client, with the definition file read from the assembly. It has no tools.</summary>
public sealed class InProcessAgentClient(IChatClient model, TimeSpan timeout) : IAgentClient
{
    private readonly AgentRunner _runner = new(model, NoTools.Source, new AgentRunnerOptions(timeout));
    private readonly Lazy<Norvia.Agents.Definitions.AgentDefinition> _definition = new(BookingIntakeDefinition.Load);

    public async Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken) =>
        (await _runner.RunReplyAsync(_definition.Value, question, cancellationToken)).Reply;
}
