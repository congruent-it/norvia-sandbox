using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings;

/// <param name="Carrier">The Carrier the booking asked, which a reassignment before the release may have changed since.</param>
/// <param name="ReasonCode">The Carrier's reason for rejecting; null unless <see cref="Status"/> is Rejected.</param>
/// <param name="ReasonText">The Carrier's reason text; null unless one was given.</param>
/// <param name="SentAt">When the Carrier acknowledged receipt, reported by the P4 booking Logic App; null until then.</param>
/// <param name="NotDeliveredStatusCode">The technical cause's status code; null unless <see cref="Status"/> is NotDelivered.</param>
/// <param name="NotDeliveredMessage">The technical cause's message; null unless <see cref="Status"/> is NotDelivered.</param>
/// <param name="WithdrawnAt">When the Planner withdrew it; null unless <see cref="Status"/> is Withdrawn.</param>
/// <param name="CarrierCost">The Carrier cost frozen on the booking at release (P9).</param>
public sealed record CarrierBookingView(
    string CarrierBookingNumber,
    CarrierView Carrier,
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
    DateTimeOffset? WithdrawnAt,
    CarrierCostView? CarrierCost = null)
{
    public static CarrierBookingView Of(CarrierBooking booking, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(
            booking.Number.Value,
            CarrierView.Of(booking.CarrierId, carriers),
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
            booking.WithdrawnAt,
            CarrierCostView.From(booking.CarrierCost));
    }
}