namespace Norvia.PartnerPortal.Application.Bookings.DraftFromText;

/// <summary>
/// A Booking draft: a <see cref="BookingRequest"/> the Intake agent filled in, and what it could not. It is not a booking until the Shipper
/// submits it, and it is not kept anywhere: the portal holds it for the form only.
/// </summary>
public sealed record BookingDraftAnswer(BookingDraftOutcome Outcome, BookingRequest? Booking, IReadOnlyList<FlaggedField> Flags, string? MessageId)
{
    public static BookingDraftAnswer Refused(BookingDraftOutcome outcome, string? messageId) => new(outcome, null, [], messageId);
}
