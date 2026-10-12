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
            if (StopOf(trackingEvent.Type) is { } stop && OutcomeKindOf(trackingEvent.Type) is { } kind)
            {
                outcomes.TryAdd((trackingEvent.ShipmentId, stop), new StopOutcome(kind, trackingEvent.EventTime, trackingEvent.Remark));
            }
        }

        return new StopOutcomes(outcomes);
    }

    public StopOutcome Of(ShipmentId shipment, StopKind kind) =>
        _outcomes.GetValueOrDefault((shipment, kind), StopOutcome.Open);

    /// <summary>The Stop a Tracking event type completes, or null for a type that is no outcome.</summary>
    public static StopKind? StopOf(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected or TrackingEventType.CollectionFailed => StopKind.Collection,
        TrackingEventType.Delivered or TrackingEventType.DeliveryFailed => StopKind.Delivery,
        _ => null,
    };

    private static StopOutcomeKind? OutcomeKindOf(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected => StopOutcomeKind.Collected,
        TrackingEventType.CollectionFailed => StopOutcomeKind.CollectionFailed,
        TrackingEventType.Delivered => StopOutcomeKind.Delivered,
        TrackingEventType.DeliveryFailed => StopOutcomeKind.DeliveryFailed,
        _ => null,
    };
}
