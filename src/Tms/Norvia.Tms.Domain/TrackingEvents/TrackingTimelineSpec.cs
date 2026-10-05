using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>A Shipment's Tracking events in event-time order, the order they arrived breaking a tie (P5).</summary>
public sealed class TrackingTimelineSpec : Specification<TrackingEvent>
{
    public TrackingTimelineSpec(ShipmentId shipmentId)
    {
        Query.Where(trackingEvent => trackingEvent.ShipmentId == shipmentId)
            .OrderBy(trackingEvent => trackingEvent.EventTime)
            .ThenBy(trackingEvent => trackingEvent.ReceivedAt)
            .AsNoTracking();
    }
}
