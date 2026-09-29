using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>Carrier bookings for a set of Shipments, most recently requested first, to find each Shipment's last one (P4).</summary>
public sealed class CarrierBookingsForShipmentsSpec : Specification<CarrierBooking>
{
    public CarrierBookingsForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(booking => shipmentIds.Contains(booking.ShipmentId)).OrderByDescending(booking => booking.RequestedAt).AsNoTracking();
    }
}
