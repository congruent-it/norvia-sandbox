using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// The Canonical Model event <c>ShipmentInTransit</c>, version 1 (contracts/canonical-model/shipment-in-transit.v1.schema.json).
/// A Tracking event moved a Shipment to In transit (P5): the goods are on their way to the delivery address.
/// </summary>
public sealed record ShipmentInTransitV1(
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
    public override string EventType => "ShipmentInTransit";

    public static ShipmentInTransitV1 From(ShipmentInTransitEvent inTransit) =>
        From(inTransit.Shipment, inTransit.TrackingEvent, static (id, at, correlationId, shipment, order, carrier, reference, time, location, eta) =>
            new ShipmentInTransitV1(id, at, correlationId, shipment, order, carrier, reference, time, location, eta));
}
