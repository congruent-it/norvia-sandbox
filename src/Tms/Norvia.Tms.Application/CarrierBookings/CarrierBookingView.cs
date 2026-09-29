using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

/// <param name="ReasonCode">The Carrier's reason for rejecting; null unless <see cref="Status"/> is Rejected.</param>
/// <param name="ReasonText">The Carrier's reason text; null unless one was given.</param>
/// <param name="SentAt">When the Carrier acknowledged receipt, reported by the P4 booking Logic App; null until then.</param>
/// <param name="NotDeliveredStatusCode">The technical cause's status code; null unless <see cref="Status"/> is NotDelivered.</param>
/// <param name="NotDeliveredMessage">The technical cause's message; null unless <see cref="Status"/> is NotDelivered.</param>
/// <param name="WithdrawnAt">When the Planner withdrew it; null unless <see cref="Status"/> is Withdrawn.</param>
public sealed record CarrierBookingView(
    string CarrierBookingNumber,
    CarrierBookingStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AnsweredAt,
    string? CarrierBookingReference,
    DateTimeOffset? PlannedCollectionTime,
    CarrierBookingRejectionReason? ReasonCode,
    string? ReasonText,
    DateTimeOffset? SentAt,
    int? NotDeliveredStatusCode,
    string? NotDeliveredMessage,
    DateTimeOffset? WithdrawnAt)
{
    public static CarrierBookingView Of(CarrierBooking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(
            booking.Number.Value,
            booking.Status,
            booking.RequestedAt,
            booking.AnsweredAt,
            booking.CarrierBookingReference,
            booking.PlannedCollectionTime,
            booking.RejectionReason,
            booking.RejectionReasonText,
            booking.SentAt,
            booking.NotDeliveredStatusCode,
            booking.NotDeliveredMessage,
            booking.WithdrawnAt);
    }
}

