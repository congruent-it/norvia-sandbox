using Norvia.PartnerPortal.Domain.Bookings;

namespace Norvia.PartnerPortal.Application.Bookings;

/// <summary>A booking the Persona sent, and Norvia's answer.</summary>
public sealed record BookingView(
    Guid Id,
    string? ShipperReference,
    DateTimeOffset SentAt,
    BookingResult Result,
    string? TransportOrderNumber,
    IReadOnlyList<RejectionReason> Reasons,
    string? Text,
    string? MessageId)
{
    /// <summary>False when the portal couldn't keep its record of the booking, so it isn't in My bookings. Norvia's answer stands.</summary>
    public bool Recorded { get; init; } = true;

    public static BookingView From(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var answer = booking.Answer;
        return new(booking.Id.Value, booking.ShipperReference, booking.SentAt, answer.Result, answer.TransportOrderNumber, answer.Reasons, answer.Text, answer.MessageId);
    }
}
