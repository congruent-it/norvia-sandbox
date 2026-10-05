namespace Norvia.Tms.Domain.Shipments;

/// <summary>P5's Stale rules, with the thresholds from the TMS settings.</summary>
public sealed class ShipmentStaleRules(TimeSpan collectionGrace, TimeSpan silenceLimit, TimeSpan deliveryGrace)
{
    /// <summary>
    /// A Shipment on its way is Stale when its next expected Tracking event is late: a Booked one not collected the
    /// collection grace after its collection window's latest time; a Collected or In transit one with no Tracking event
    /// for the silence limit; any one not Delivered the delivery grace after its delivery window's latest time. It is
    /// worked out from the Shipment and its events each time, so it clears once a Tracking event makes its rule untrue.
    /// Stale is only a flag: nothing acts on it.
    /// </summary>
    /// <param name="collectionWindowLatest">The Transport Order's collection window's latest time, if it has one.</param>
    /// <param name="deliveryWindowLatest">The Transport Order's delivery window's latest time, if it has one.</param>
    /// <param name="lastEventReceivedAt">When the TMS received the last Tracking event, if any.</param>
    public bool IsStale(
        ShipmentStatus status,
        DateTimeOffset? collectionWindowLatest,
        DateTimeOffset? deliveryWindowLatest,
        DateTimeOffset? lastEventReceivedAt,
        DateTimeOffset now)
    {
        if (status is not (ShipmentStatus.Booked or ShipmentStatus.Collected or ShipmentStatus.InTransit))
        {
            return false;
        }

        var notCollected = status == ShipmentStatus.Booked && collectionWindowLatest is { } collection && now >= collection + collectionGrace;
        var silent = status != ShipmentStatus.Booked && lastEventReceivedAt is { } last && now >= last + silenceLimit;
        var notDelivered = deliveryWindowLatest is { } delivery && now >= delivery + deliveryGrace;
        return notCollected || silent || notDelivered;
    }
}
