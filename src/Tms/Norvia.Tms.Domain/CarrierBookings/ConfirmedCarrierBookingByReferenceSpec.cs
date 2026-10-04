using Ardalis.Specification;
using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>
/// The Confirmed Carrier booking of one Carrier that has the Carrier's own booking reference, which is how a Tracking
/// event finds its Shipment (P5). Scoped to the Carrier, as two Carriers may use the same reference.
/// </summary>
public sealed class ConfirmedCarrierBookingByReferenceSpec : SingleResultSpecification<CarrierBooking>
{
    public ConfirmedCarrierBookingByReferenceSpec(CarrierId carrierId, string carrierBookingReference)
    {
        CarrierId = carrierId;
        CarrierBookingReference = carrierBookingReference;
        Query.Where(booking =>
            booking.CarrierId == carrierId
            && booking.CarrierBookingReference == carrierBookingReference
            && booking.Status == CarrierBookingStatus.Confirmed)
            .AsNoTracking();
    }

    public CarrierId CarrierId { get; }

    public string CarrierBookingReference { get; }
}
