using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>When a Shipment is expected to be delivered (P5).</summary>
public static class ShipmentEta
{
    /// <summary>
    /// The ETA of the last-arrived Tracking event that carried one, so a late report of an older event can't overrule a
    /// newer one; the delivery window's latest time when none did; null when the Transport Order has no delivery window.
    /// </summary>
    public static DateTimeOffset? Of(IEnumerable<TrackingEvent> events, TimeWindow? deliveryWindow)
    {
        ArgumentNullException.ThrowIfNull(events);
        var lastReported = events.Where(trackingEvent => trackingEvent.Eta is not null).MaxBy(trackingEvent => trackingEvent.ReceivedAt);
        return lastReported?.Eta ?? deliveryWindow?.Latest;
    }
}
