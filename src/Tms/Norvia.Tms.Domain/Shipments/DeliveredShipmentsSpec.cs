using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Delivered Shipments (P6), for finding the ones still without a Proof of delivery.</summary>
public sealed class DeliveredShipmentsSpec : Specification<Shipment>
{
    public DeliveredShipmentsSpec()
    {
        Query.Where(shipment => shipment.Status == ShipmentStatus.Delivered).AsNoTracking();
    }
}
