using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.HealthCheck.Application.Agents;

namespace Norvia.HealthCheck.Infrastructure.Agents;

/// <summary>The port while the Agents are off. Nothing asks for it then, as the page shows no narration.</summary>
internal sealed class DisabledAgentClient : IAgentClient
{
    public Task<Result<Narration>> NarrateAsync(AgentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Narration>.Unavailable(AgentFailureCodes.AgentUnavailable));
}
