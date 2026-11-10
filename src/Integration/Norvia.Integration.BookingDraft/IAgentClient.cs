using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft;

/// <summary>
/// The port to the Intake agent (ADR 0040): one question, one reply as the Agent wrote it. Locally the in-process runner answers it with the
/// scripted model (ADR 0041); in Azure the host's Foundry client does. A failure is an exception: the extractor turns any of them into
/// <see cref="BookingDraftFailureCodes.AgentUnavailable"/>, because the Agent is never on a critical path.
/// </summary>
public interface IAgentClient
{
    /// <summary>The Agent's reply as it wrote it: a code fence around it is the extractor's to remove. The question's text is never logged by an implementation.</summary>
    Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken);
}
