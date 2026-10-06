using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>
/// A Shipment's last Carrier booking's outcome, once it has ended without being carried out: rejected, not delivered or withdrawn
/// (P4), or cancelled by Norvia after the Carrier confirmed it (P7).
/// </summary>
/// <param name="ReasonCode">The Carrier's reason for rejecting; null unless it rejected.</param>
/// <param name="ReasonText">The Carrier's reason text for rejecting, or Norvia's reason for cancelling; null when none was given.</param>
public sealed record LastCarrierBookingOutcome(CarrierBookingStatus Status, CarrierView Carrier, CarrierBookingRejectionReason? ReasonCode, string? ReasonText)
{
    public static LastCarrierBookingOutcome Of(CarrierBooking booking, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(booking.Status, CarrierView.Of(booking.CarrierId, carriers), booking.RejectionReason, booking.CancellationReasonText ?? booking.RejectionReasonText);
    }
}
