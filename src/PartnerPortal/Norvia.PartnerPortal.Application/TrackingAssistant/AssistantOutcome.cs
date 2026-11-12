namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>How a run ended, as the tracking contract's <c>outcome</c> names it.</summary>
public enum AssistantOutcome
{
    /// <summary>The Shipper was shown an answer (or the assistant's honest "no information").</summary>
    Answered,

    /// <summary>A limit stopped the question before the model was called.</summary>
    Refused,

    /// <summary>Tracking, the model or the check failed.</summary>
    Failed,
}
