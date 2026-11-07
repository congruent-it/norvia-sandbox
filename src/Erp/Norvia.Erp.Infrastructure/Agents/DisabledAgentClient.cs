using Ardalis.Result;
using Norvia.Agents;
using Norvia.Agents.Runner;
using Norvia.Erp.Application.Agents;

namespace Norvia.Erp.Infrastructure.Agents;

/// <summary>The port while the Agents are off. Nothing asks for it then, as the ERP UI shows no Explain button.</summary>
internal sealed class DisabledAgentClient : IAgentClient
{
    public Task<Result<Suggestion>> AskAsync(AgentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<Suggestion>.Unavailable(AgentFailureCodes.AgentUnavailable));
}
