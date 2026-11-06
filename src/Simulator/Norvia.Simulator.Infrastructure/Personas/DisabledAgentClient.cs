using Ardalis.Result;
using Norvia.Agents.Runner;
using Norvia.Simulator.Application.Ports;

namespace Norvia.Simulator.Infrastructure.Personas;

/// <summary>The port while the Agents are off (<c>Agents:Enabled</c>). A plan then asks for no generation, so nothing calls it.</summary>
internal sealed class DisabledAgentClient : IAgentClient
{
    public Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Unavailable(AgentFailureCodes.AgentUnavailable));
}
