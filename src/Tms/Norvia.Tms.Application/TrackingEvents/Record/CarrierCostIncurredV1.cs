using System.Text.Json.Serialization;
using Norvia.Tms.Application.CarrierBookings;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// The Canonical Model event <c>CarrierCostIncurred</c>, version 1 (contracts/canonical-model/carrier-cost-incurred.v1.schema.json).
/// A Carrier Shipment moved to Delivered, so the Carrier earned the Carrier cost frozen on the Confirmed Carrier booking that
/// carried it (P9). It carries the Delivered Tracking event's correlation ID and doesn't wait for a Proof of delivery.
/// </summary>
/// <param name="OccurredAt">When the TMS recorded the Delivered Tracking event.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking.</param>
/// <param name="DeliveryTime">When the goods were delivered, with the offset of where.</param>
/// <param name="SourceMessageId">The Carrier's ID for the Delivered Tracking event; it travels as a tracking property, not in the body.</param>
public sealed record CarrierCostIncurredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CarrierBookingNumber,
    string CarrierBookingReference,
    string ShipmentNumber,
    string TransportOrderNumber,
    string CarrierId,
    CarrierCostView CarrierCost,
    DateTimeOffset DeliveryTime,
    [property: JsonIgnore] string SourceMessageId)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "CarrierCostIncurred";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static CarrierCostIncurredV1 From(CarrierCostIncurredEvent incurred)
    {
        ArgumentNullException.ThrowIfNull(incurred);
        var delivered = incurred.DeliveredEvent;
        var booking = incurred.Booking;
        return new(
            Guid.CreateVersion7(),
            delivered.ReceivedAt,
            delivered.CorrelationId.Value,
            booking.Number.Value,
            booking.CarrierBookingReference!,
            incurred.Shipment.Number.Value,
            incurred.Shipment.TransportOrderNumber.Value,
            booking.CarrierId.Value,
            CarrierCostView.From(incurred.CarrierCost)!,
            delivered.EventTime,
            delivered.SourceEventId);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.CarrierTracking),
        new(TrackingKeys.PartyId, CarrierId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.CarrierBookingNumber, CarrierBookingNumber),
        new(TrackingKeys.SourceMessageId, SourceMessageId),
    ];

    // The Carrier cost Logic App's subscription filters on it: only Carrier Shipments have a Carrier cost.
    protected override IEnumerable<KeyValuePair<string, object>> FilterProperties() =>
    [
        new(ShipmentEventProperties.AssignedTo, AssignedTo.Carrier.ToString()),
    ];
}
