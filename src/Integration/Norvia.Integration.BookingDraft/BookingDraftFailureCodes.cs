namespace Norvia.Integration.BookingDraft;

/// <summary>The stable failure codes of a Booking draft (docs/architecture/failure-behaviour.md).</summary>
public static class BookingDraftFailureCodes
{
    /// <summary>The text describes more than one booking; the Shipper pastes one at a time.</summary>
    public const string MultipleBookingsInText = "MultipleBookingsInText";

    /// <summary>The model is down or too slow, or its reply is not a draft that matches the schema.</summary>
    public const string AgentUnavailable = "AgentUnavailable";
}
