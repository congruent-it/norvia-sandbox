using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>What recording a Tracking event did: the Shipment it was for, its status now, and whether the event moved it.</summary>
/// <param name="EventId">The reporter's own ID for the event.</param>
/// <param name="MovedShipment">True when the event moved the Shipment's status; an event that doesn't is only logged.</param>
/// <param name="CorrelationId">The event's own trail; for a repeat, the trail of the event it repeats.</param>
/// <param name="Repeat">True when the event was sent again under the same event ID, and answered as before without recording anything again.</param>
public sealed record RecordTrackingResult(
    string ShipmentNumber,
    ShipmentStatus ShipmentStatus,
    string EventId,
    bool MovedShipment,
    string CorrelationId,
    bool Repeat = false)
{
    public static RecordTrackingResult From(Shipment shipment, TrackingEvent trackingEvent, bool repeat) =>
        new(shipment.Number.Value, shipment.Status, trackingEvent.SourceEventId, trackingEvent.MovedShipment, trackingEvent.CorrelationId.Value, repeat);
}
