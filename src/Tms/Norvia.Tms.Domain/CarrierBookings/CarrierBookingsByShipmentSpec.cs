using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>A Shipment's Carrier bookings, oldest first: only one in this build, but there will be several over its life.</summary>
public sealed class CarrierBookingsByShipmentSpec : Specification<CarrierBooking>
{
    public CarrierBookingsByShipmentSpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(booking => booking.ShipmentId == shipmentId).OrderBy(booking => booking.RequestedAt).AsNoTracking();
    }

    public ShipmentId ShipmentId { get; }
}
