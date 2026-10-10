using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The tracked Shipments with these IDs, which Dispatch changes.</summary>
public sealed class ShipmentsToDispatchSpec : Specification<Shipment>
{
    public ShipmentsToDispatchSpec(IReadOnlyCollection<ShipmentId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Query.Where(shipment => ids.Contains(shipment.Id));
    }
}
