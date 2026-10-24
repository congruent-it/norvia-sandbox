using Ardalis.Result;
using Norvia.Agents;

namespace Norvia.IntegrationConsole.Application.Agents;

/// <summary>
/// The console's port to an Agent (ADR 0040). Infrastructure implements it: locally by the in-process runner against the MCP
/// stand-in (ADR 0041), in Azure with the Foundry client. A failure comes back as <see cref="ResultStatus.Unavailable"/>
/// whose error is the failure code (<c>AgentUnavailable</c> or <c>AgentToolRefused</c>); an Agent is never on a critical path.
/// </summary>
public interface IAgentClient
{
    Task<Result<Suggestion>> AskAsync(AgentRequest request, CancellationToken cancellationToken);
}

/// <summary>One question to an Agent about a Message trail.</summary>
/// <param name="AgentName">The Agent's definition, such as <c>integration-operations</c>.</param>
/// <param name="CorrelationId">The Message trail the question is about, which the Agent's tool calls carry so they join it.</param>
public sealed record AgentRequest(string AgentName, string Question, string CorrelationId);
