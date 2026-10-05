using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>When a Shipment was delivered: the event time of the Delivered Tracking event that moved it there (P6).</summary>
public sealed record DeliveredEventRow(ShipmentId ShipmentId, DateTimeOffset EventTime);

/// <summary>
/// The Delivered Tracking events that moved several Shipments to Delivered, for enriching a list built from them (P6). A
/// later repeat of the event, which moved nothing, doesn't count.
/// </summary>
public sealed class DeliveredTrackingEventsForShipmentsSpec : Specification<TrackingEvent, DeliveredEventRow>
{
    public DeliveredTrackingEventsForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(trackingEvent => trackingEvent.Type == TrackingEventType.Delivered && trackingEvent.MovedShipment && shipmentIds.Contains(trackingEvent.ShipmentId))
            .Select(trackingEvent => new DeliveredEventRow(trackingEvent.ShipmentId, trackingEvent.EventTime));
    }
}
