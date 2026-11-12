namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>One run of the Agent. The input holds the question, so an implementation never logs it.</summary>
/// <param name="RunId">The run's ID, which the portal's span records.</param>
/// <param name="MaxOutputTokens">The cap on the answer.</param>
public sealed record AssistantRequest(string RunId, AssistantInput Input, int MaxOutputTokens);
