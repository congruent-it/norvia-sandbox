using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Own fleet Shipments that are Released, in the order they were planned: the ones a Planner can put on a Trip (P11), if they aren't on one yet.</summary>
public sealed class ReleasedOwnFleetShipmentsSpec : Specification<Shipment>
{
    public ReleasedOwnFleetShipmentsSpec()
    {
        Query.Where(shipment => shipment.Status == ShipmentStatus.Released && shipment.CarriedBy.CarrierId == null)
            .OrderBy(shipment => shipment.PlannedAt)
            .AsNoTracking();
    }
}
