using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>The Delivered Tracking events of several Shipments, for enriching a list built from them (P6).</summary>
public sealed class DeliveredTrackingEventsForShipmentsSpec : Specification<TrackingEvent>
{
    public DeliveredTrackingEventsForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(trackingEvent => trackingEvent.Type == TrackingEventType.Delivered && shipmentIds.Contains(trackingEvent.ShipmentId)).AsNoTracking();
    }
}
