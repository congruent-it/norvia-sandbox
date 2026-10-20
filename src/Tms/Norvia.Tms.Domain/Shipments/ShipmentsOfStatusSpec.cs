using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipments in one status, for finding the Transport Orders a list is filtered to.</summary>
public sealed class ShipmentsOfStatusSpec : Specification<Shipment>
{
    public ShipmentsOfStatusSpec(ShipmentStatus status) =>
        Query.Where(shipment => shipment.Status == status).AsNoTracking();
}
