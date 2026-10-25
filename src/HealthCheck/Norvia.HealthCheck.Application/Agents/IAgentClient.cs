using Ardalis.Result;
using Norvia.Agents;

namespace Norvia.HealthCheck.Application.Agents;

/// <summary>
/// The app's port to the Estate health-check agent (ADR 0040). Infrastructure implements it: locally by the in-process runner against
/// the MCP stand-in (ADR 0041). A failure comes back as <see cref="ResultStatus.Unavailable"/> whose error is the failure code
/// (<c>AgentUnavailable</c> or <c>AgentToolRefused</c>); an Agent is never on a critical path.
/// </summary>
public interface IAgentClient
{
    Task<Result<Narration>> NarrateAsync(AgentRequest request, CancellationToken cancellationToken);
}

/// <summary>One question to an Agent.</summary>
/// <param name="AgentName">The Agent's definition, such as <c>estate-health-check</c>.</param>
/// <param name="CorrelationId">The ID of the health check run, which the Agent's tool calls carry so they join its Message trail.</param>
public sealed record AgentRequest(string AgentName, string Question, string CorrelationId);
