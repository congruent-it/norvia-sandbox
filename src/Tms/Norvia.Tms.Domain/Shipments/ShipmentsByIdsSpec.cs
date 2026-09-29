using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>Shipments by their IDs, for enriching a list built from another aggregate (P4).</summary>
public sealed class ShipmentsByIdsSpec : Specification<Shipment>
{
    public ShipmentsByIdsSpec(IReadOnlyCollection<ShipmentId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Query.Where(shipment => ids.Contains(shipment.Id)).AsNoTracking();
    }
}
