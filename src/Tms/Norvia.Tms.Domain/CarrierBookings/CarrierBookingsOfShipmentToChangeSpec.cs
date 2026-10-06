using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>A Shipment's Carrier bookings, most recently requested first, tracked, so cancelling the Confirmed one saves its change with it (P7).</summary>
public sealed class CarrierBookingsOfShipmentToChangeSpec : Specification<CarrierBooking>
{
    public CarrierBookingsOfShipmentToChangeSpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(booking => booking.ShipmentId == shipmentId).OrderByDescending(booking => booking.RequestedAt);
    }

    public ShipmentId ShipmentId { get; }
}
