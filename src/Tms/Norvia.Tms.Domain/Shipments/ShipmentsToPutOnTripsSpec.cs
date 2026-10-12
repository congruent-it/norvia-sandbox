using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Own fleet Shipments that are Released or Awaiting re-delivery (P12), in the order they were planned: the ones a Planner can put on a Trip (P11), if they aren't on one yet.</summary>
public sealed class ShipmentsToPutOnTripsSpec : Specification<Shipment>
{
    public ShipmentsToPutOnTripsSpec()
    {
        Query.Where(shipment => (shipment.Status == ShipmentStatus.Released || shipment.AwaitingRedelivery) && shipment.CarriedBy.CarrierId == null)
            .OrderBy(shipment => shipment.PlannedAt)
            .AsNoTracking();
    }
}
