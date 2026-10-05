using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// The Canonical Model event <c>ShipmentExceptionReported</c>, version 1 (contracts/canonical-model/shipment-exception-reported.v1.schema.json).
/// An exception Tracking event (Collection failed, Delivery failed or Damaged) was recorded for a Shipment (P5). The
/// Shipment's status is unchanged.
/// </summary>
/// <param name="ExceptionType">Which exception: <c>CollectionFailed</c>, <c>DeliveryFailed</c> or <c>Damaged</c>.</param>
/// <param name="Remark">The Carrier's remark; null when it gave none.</param>
public sealed record ShipmentExceptionReportedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string? CarrierId,
    string? CarrierBookingReference,
    DateTimeOffset EventTime,
    TrackingLocationV1 Location,
    DateTimeOffset? Eta,
    string ExceptionType,
    string? Remark)
    : ShipmentProgressV1(EventId, OccurredAt, CorrelationId, ShipmentNumber, TransportOrderNumber, CarrierId, CarrierBookingReference, EventTime, Location, Eta)
{
    public override string EventType => "ShipmentExceptionReported";

    public static ShipmentExceptionReportedV1 From(ShipmentExceptionReportedEvent reported)
    {
        var trackingEvent = reported.TrackingEvent;
        return From(reported.Shipment, trackingEvent, (id, at, correlationId, shipment, order, carrier, reference, time, location, eta) =>
            new ShipmentExceptionReportedV1(id, at, correlationId, shipment, order, carrier, reference, time, location, eta, trackingEvent.Type.ToString(), trackingEvent.Remark));
    }
}
