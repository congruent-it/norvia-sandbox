using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

/// <param name="ReasonCode">The Carrier's reason for rejecting; null unless <see cref="Status"/> is Rejected.</param>
/// <param name="ReasonText">The Carrier's reason text; null unless one was given.</param>
public sealed record CarrierBookingView(
    string CarrierBookingNumber,
    CarrierBookingStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AnsweredAt,
    string? CarrierBookingReference,
    DateTimeOffset? PlannedCollectionTime,
    CarrierBookingRejectionReason? ReasonCode,
    string? ReasonText)
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
            booking.RejectionReasonText);
    }
}
