using Ardalis.Result;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>
/// The Partner portal's port to the Shipper tracking assistant's Agent (ADR 0049). The Agent has no tool and no credential: it gets the input and
/// answers once. Infrastructure implements it, locally with the scripted fake. A failure comes back as <see cref="ResultStatus.Unavailable"/>
/// whose error is the failure code (<c>AgentUnavailable</c>); an Agent is never on a critical path.
/// </summary>
public interface IAgentClient
{
    Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken cancellationToken);
}
