using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipments on their way (P5): Booked, Dispatched, Collected and In transit, in the order they were planned. A Delivered one is not.</summary>
public sealed class ShipmentsInProgressSpec : Specification<Shipment>
{
    // Named, not a range: the status is stored by name, so the database would compare the names, not the order they come in.
    private static readonly ShipmentStatus[] InProgress = [ShipmentStatus.Booked, ShipmentStatus.Dispatched, ShipmentStatus.Collected, ShipmentStatus.InTransit];

    public ShipmentsInProgressSpec()
    {
        Query.Where(shipment => InProgress.Contains(shipment.Status))
            .OrderBy(shipment => shipment.PlannedAt)
            .AsNoTracking();
    }
}
