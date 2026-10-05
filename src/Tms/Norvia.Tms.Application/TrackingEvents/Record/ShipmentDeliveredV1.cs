using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// The Canonical Model event <c>ShipmentDelivered</c>, version 1 (contracts/canonical-model/shipment-delivered.v1.schema.json).
/// A Tracking event moved a Shipment to Delivered (P5): the goods have been delivered, which doesn't wait for a Proof of delivery.
/// </summary>
public sealed record ShipmentDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string? CarrierId,
    string? CarrierBookingReference,
    DateTimeOffset EventTime,
    TrackingLocationV1 Location,
    DateTimeOffset? Eta)
    : ShipmentProgressV1(EventId, OccurredAt, CorrelationId, ShipmentNumber, TransportOrderNumber, CarrierId, CarrierBookingReference, EventTime, Location, Eta)
{
    public override string EventType => "ShipmentDelivered";

    public static ShipmentDeliveredV1 From(ShipmentDeliveredEvent delivered) =>
        From(delivered.Shipment, delivered.TrackingEvent, static (id, at, correlationId, shipment, order, carrier, reference, time, location, eta) =>
            new ShipmentDeliveredV1(id, at, correlationId, shipment, order, carrier, reference, time, location, eta));
}
