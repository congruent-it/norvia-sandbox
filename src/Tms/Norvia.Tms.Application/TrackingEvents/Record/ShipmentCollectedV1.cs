using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// The Canonical Model event <c>ShipmentCollected</c>, version 1 (contracts/canonical-model/shipment-collected.v1.schema.json).
/// A Tracking event moved a Booked Shipment to Collected (P5).
/// </summary>
public sealed record ShipmentCollectedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string? CarrierId,
    string? CarrierBookingReference,
    string? TripNumber,
    DateTimeOffset EventTime,
    TrackingLocationV1 Location,
    DateTimeOffset? Eta)
    : ShipmentProgressV1(EventId, OccurredAt, CorrelationId, ShipmentNumber, TransportOrderNumber, CarrierId, CarrierBookingReference, TripNumber, EventTime, Location, Eta)
{
    public override string EventType => "ShipmentCollected";

    public static ShipmentCollectedV1 From(ShipmentCollectedEvent collected) =>
        From(collected.Shipment, collected.TrackingEvent, static (id, at, correlationId, shipment, order, carrier, reference, tripNumber, time, location, eta) =>
            new ShipmentCollectedV1(id, at, correlationId, shipment, order, carrier, reference, tripNumber, time, location, eta));
}
