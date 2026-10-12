namespace Norvia.Tms.Domain.Shipments;

/// <summary>P5's Stale rules, with the thresholds from the TMS settings.</summary>
public sealed class ShipmentStaleRules(TimeSpan collectionGrace, TimeSpan silenceLimit, TimeSpan deliveryGrace)
{
    /// <summary>
    /// A Shipment on its way is Stale when its next expected Tracking event is late: a Booked or Dispatched one (awaiting collection) not
    /// collected the collection grace after its collection window's latest time; a Collected or In transit one with no Tracking event
    /// for the silence limit; any one not Delivered the delivery grace after its delivery window's latest time. It is
    /// worked out from the Shipment and its events each time, so it clears once a Tracking event makes its rule untrue.
    /// Stale is only a flag: nothing acts on it. A Shipment Awaiting re-delivery (P12) is never Stale: Late delivery (P7) covers the wait.
    /// </summary>
    /// <param name="collectionWindowLatest">The Transport Order's collection window's latest time, if it has one.</param>
    /// <param name="deliveryWindowLatest">The Transport Order's delivery window's latest time, if it has one.</param>
    /// <param name="lastEventReceivedAt">When the TMS received the last Tracking event, if any.</param>
    public bool IsStale(
        ShipmentStatus status,
        DateTimeOffset? collectionWindowLatest,
        DateTimeOffset? deliveryWindowLatest,
        DateTimeOffset? lastEventReceivedAt,
        DateTimeOffset now,
        bool awaitingRedelivery = false)
    {
        if (awaitingRedelivery || status is not (ShipmentStatus.Booked or ShipmentStatus.Dispatched or ShipmentStatus.Collected or ShipmentStatus.InTransit))
        {
            return false;
        }

        var awaitingCollection = status is ShipmentStatus.Booked or ShipmentStatus.Dispatched;
        var notCollected = awaitingCollection && collectionWindowLatest is { } collection && now >= collection + collectionGrace;
        var silent = !awaitingCollection && lastEventReceivedAt is { } last && now >= last + silenceLimit;
        var notDelivered = deliveryWindowLatest is { } delivery && now >= delivery + deliveryGrace;
        return notCollected || silent || notDelivered;
    }
}
