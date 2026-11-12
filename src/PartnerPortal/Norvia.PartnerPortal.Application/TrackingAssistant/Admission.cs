namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>Whether a question may reach the model.</summary>
public enum Admission
{
    Allowed,

    /// <summary>Over the Persona's rate, the question size or its daily token budget: <c>AgentRateLimited</c>.</summary>
    RateLimited,

    /// <summary>The estate's daily token budget is used up, so every Persona sees "Assistant unavailable": <c>AgentUnavailable</c>.</summary>
    EstateBudgetUsed,
}
