using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>The Tracking events of several Shipments, for enriching a list built from them (P5).</summary>
public sealed class TrackingEventsForShipmentsSpec : Specification<TrackingEvent>
{
    public TrackingEventsForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(trackingEvent => shipmentIds.Contains(trackingEvent.ShipmentId)).AsNoTracking();
    }
}
