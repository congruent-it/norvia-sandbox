using Ardalis.Result;
using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Infrastructure.Assistant;

/// <summary>The port while <c>Agents:Enabled</c> is off. Nothing asks for it then, as the tracking screen shows no panel.</summary>
internal sealed class DisabledAgentClient : IAgentClient
{
    public Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result<AssistantReply>.Unavailable(AssistantFailureCodes.AgentUnavailable));
}
