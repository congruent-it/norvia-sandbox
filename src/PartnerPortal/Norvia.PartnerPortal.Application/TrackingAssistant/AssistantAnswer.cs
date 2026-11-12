namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>What the Shipper is shown for a question: the answer, or one of the fixed texts.</summary>
/// <param name="Text">The assistant's answer when <see cref="Outcome"/> is <see cref="AssistantOutcome.Answered"/> and the model said something, or a fixed text.</param>
/// <param name="FailureCode">The stable code when the run was refused or failed.</param>
public sealed record AssistantAnswer(AssistantOutcome Outcome, string Text, string? FailureCode, string? RunId);
