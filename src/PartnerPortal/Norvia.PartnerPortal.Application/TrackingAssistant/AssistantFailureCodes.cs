namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The stable failure codes of the assistant (ADR 0049, ADR 0040) beside <c>TmsUnavailable</c>.</summary>
public static class AssistantFailureCodes
{
    public const string AgentUnavailable = "AgentUnavailable";
    public const string AgentRateLimited = "AgentRateLimited";
    public const string AgentAnswerRejected = "AgentAnswerRejected";
    public const string TmsUnavailable = "TmsUnavailable";
}
