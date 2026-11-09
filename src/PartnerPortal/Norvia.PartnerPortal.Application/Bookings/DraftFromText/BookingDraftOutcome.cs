namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>How Norvia's Intake agent answered a Shipper's pasted text.</summary>
public enum BookingDraftOutcome
{
    /// <summary>A Booking draft came back.</summary>
    Drafted,

    /// <summary>The text describes more than one booking; paste one at a time.</summary>
    MultipleBookings,

    /// <summary>The text is longer than the assistant accepts.</summary>
    TooLarge,

    /// <summary>The assistant is off, refused the call, is down, or gave a reply that was not a draft. Nothing is drafted.</summary>
    Unavailable,
}
