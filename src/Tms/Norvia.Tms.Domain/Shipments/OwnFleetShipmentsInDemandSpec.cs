using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipments the Own fleet has been given and not yet carried out (P10): Own fleet ones that are Planned or Released, in the order they were planned.</summary>
public sealed class OwnFleetShipmentsInDemandSpec : Specification<Shipment>
{
    // Named, not a range: the status is stored by name, so the database would compare the names, not the order they come in.
    private static readonly ShipmentStatus[] InDemand = [ShipmentStatus.Planned, ShipmentStatus.Released];

    public OwnFleetShipmentsInDemandSpec()
    {
        Query.Where(shipment => InDemand.Contains(shipment.Status) && shipment.CarriedBy.CarrierId == null)
            .OrderBy(shipment => shipment.PlannedAt)
            .AsNoTracking();
    }
}
