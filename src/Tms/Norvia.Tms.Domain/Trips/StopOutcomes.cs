using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// The outcome of each Stop of one Trip, read from the Tracking events the Trip recorded (P12, ADR 0007): a Stop holds no outcome of its
/// own. Collected and Collection failed are the outcomes of a Shipment's collection Stop, Delivered and Delivery failed of its delivery
/// Stop. Damaged is no outcome.
/// </summary>
public sealed class StopOutcomes
{
    private readonly Dictionary<(ShipmentId Shipment, StopKind Kind), StopOutcome> _outcomes;

    private StopOutcomes(Dictionary<(ShipmentId, StopKind), StopOutcome> outcomes) => _outcomes = outcomes;

    /// <summary>Reads the outcomes from the Trip's Tracking events. Were a Stop to have two, the first recorded one counts.</summary>
    public static StopOutcomes From(IEnumerable<TrackingEvent> tripEvents)
    {
        ArgumentNullException.ThrowIfNull(tripEvents);
        var outcomes = new Dictionary<(ShipmentId, StopKind), StopOutcome>();
        foreach (var trackingEvent in tripEvents.OrderBy(trackingEvent => trackingEvent.ReceivedAt))
        {
            if (Classify(trackingEvent.Type) is var (stop, kind))
            {
                outcomes.TryAdd((trackingEvent.ShipmentId, stop), new StopOutcome(kind, trackingEvent.EventTime, trackingEvent.Remark));
            }
        }

        return new StopOutcomes(outcomes);
    }

    public StopOutcome Of(ShipmentId shipment, StopKind kind) =>
        _outcomes.GetValueOrDefault((shipment, kind), StopOutcome.Open);

    /// <summary>How many of the Trip's Stops have an outcome.</summary>
    public int CountDone(Trip trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return trip.Stops.Count(stop => !Of(stop.ShipmentId, stop.Kind).IsOpen);
    }

    /// <summary>
    /// The Stops of the Trip that still wait for an outcome. The delivery Stop of a Shipment whose collection failed waits for none: the
    /// Driver never had the goods, so it can't be attempted.
    /// </summary>
    public IReadOnlyList<TripStop> OpenStopsOf(Trip trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return
        [
            .. trip.Stops.Where(stop => Of(stop.ShipmentId, stop.Kind).IsOpen
                && !(stop.Kind == StopKind.Delivery && Of(stop.ShipmentId, StopKind.Collection).Kind == StopOutcomeKind.CollectionFailed)),
        ];
    }

    /// <summary>What became of the Shipment on the Trip, once every Stop has its outcome.</summary>
    public ShipmentTripResult ResultOf(ShipmentId shipment)
    {
        if (Of(shipment, StopKind.Collection).Kind == StopOutcomeKind.CollectionFailed)
        {
            return ShipmentTripResult.NotCollected;
        }

        return Of(shipment, StopKind.Delivery).Kind == StopOutcomeKind.Delivered ? ShipmentTripResult.Delivered : ShipmentTripResult.NotDelivered;
    }

    /// <summary>The Stop a Tracking event type completes, or null for a type that is no outcome.</summary>
    public static StopKind? StopOf(TrackingEventType type) => Classify(type)?.Stop;

    private static (StopKind Stop, StopOutcomeKind Kind)? Classify(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected => (StopKind.Collection, StopOutcomeKind.Collected),
        TrackingEventType.CollectionFailed => (StopKind.Collection, StopOutcomeKind.CollectionFailed),
        TrackingEventType.Delivered => (StopKind.Delivery, StopOutcomeKind.Delivered),
        TrackingEventType.DeliveryFailed => (StopKind.Delivery, StopOutcomeKind.DeliveryFailed),
        _ => null,
    };
}
