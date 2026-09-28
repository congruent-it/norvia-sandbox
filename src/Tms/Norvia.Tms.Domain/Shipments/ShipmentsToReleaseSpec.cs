using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Planner's Shipments to release: the Planned Shipments, in the order they were planned.</summary>
public sealed class ShipmentsToReleaseSpec : Specification<Shipment>
{
    public ShipmentsToReleaseSpec()
    {
        Query.Where(shipment => shipment.Status == ShipmentStatus.Planned)
            .OrderBy(shipment => shipment.PlannedAt)
            .AsNoTracking();
    }
}
