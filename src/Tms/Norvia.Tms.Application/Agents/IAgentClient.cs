using Ardalis.Result;
using Norvia.Agents;

namespace Norvia.Tms.Application.Agents;

/// <summary>
/// The TMS UI's port to an Agent (ADR 0040). Infrastructure implements it: locally by the in-process runner against the MCP
/// stand-in (ADR 0041); the Foundry client comes with the Azure slice. A failure comes back as <see cref="ResultStatus.Unavailable"/>
/// whose error is the failure code (<c>AgentUnavailable</c> or <c>AgentToolRefused</c>); an Agent is never on a critical path.
/// </summary>
public interface IAgentClient
{
    Task<Result<Suggestion>> AskAsync(AgentRequest request, CancellationToken cancellationToken);
}

/// <summary>One question to an Agent about a Rejected booking.</summary>
/// <param name="AgentName">The Agent's definition, such as <c>customer-service</c>.</param>
/// <param name="CorrelationId">The Rejected booking's Message trail, which the Agent's tool calls carry so they join it.</param>
public sealed record AgentRequest(string AgentName, string Question, string CorrelationId);
