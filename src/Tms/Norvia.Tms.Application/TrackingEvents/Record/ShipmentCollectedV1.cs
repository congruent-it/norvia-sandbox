using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// The Canonical Model event <c>ShipmentCollected</c>, version 1 (contracts/canonical-model/shipment-collected.v1.schema.json).
/// A Tracking event moved a Booked Shipment to Collected (P5). It carries what the event said: when and where, and the ETA
/// if one was given, under the correlation ID of the call that reported it.
/// </summary>
/// <param name="OccurredAt">When the TMS recorded the event.</param>
/// <param name="CarrierId">The Carrier that reported the event.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking, which matched the event to the Shipment.</param>
/// <param name="EventTime">When the goods were collected, with the offset of where.</param>
public sealed record ShipmentCollectedV1(
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
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipmentCollected";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipmentCollectedV1 From(ShipmentCollectedEvent collected)
    {
        ArgumentNullException.ThrowIfNull(collected);
        var shipment = collected.Shipment;
        var trackingEvent = collected.TrackingEvent;
        return new(
            Guid.CreateVersion7(),
            trackingEvent.ReceivedAt,
            trackingEvent.CorrelationId.Value,
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            trackingEvent.Source.CarrierId?.Value,
            trackingEvent.CarrierBookingReference,
            trackingEvent.EventTime,
            new TrackingLocationV1(trackingEvent.Location.Place, trackingEvent.Location.Postcode, trackingEvent.Location.CountryCode),
            trackingEvent.Eta);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties()
    {
        var properties = new List<KeyValuePair<string, object>>
        {
            new(TrackingKeys.Flow, Flows.CarrierTracking),
            new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
            new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        };
        if (CarrierId is not null)
        {
            properties.Add(new(TrackingKeys.PartyId, CarrierId));
        }

        return properties;
    }

    protected override IEnumerable<KeyValuePair<string, object>> FilterProperties() =>
    [
        new(ShipmentEventProperties.AssignedTo, (CarrierId is null ? AssignedTo.OwnFleet : AssignedTo.Carrier).ToString()),
    ];
}

/// <summary>Where a Tracking event happened, as the Canonical Model names it.</summary>
public sealed record TrackingLocationV1(string Place, string Postcode, string CountryCode);
