using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.Shipments.ListToRelease;

/// <summary>A Shipment's last Carrier booking's outcome, once it has ended without being confirmed (P4).</summary>
public sealed record LastCarrierBookingOutcome(CarrierBookingStatus Status, CarrierView Carrier, CarrierBookingRejectionReason? ReasonCode, string? ReasonText)
{
    public static LastCarrierBookingOutcome Of(CarrierBooking booking, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return new(booking.Status, CarrierView.Of(booking.CarrierId, carriers), booking.RejectionReason, booking.RejectionReasonText);
    }
}
