using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>
/// What the Shipment events of a Tracking event that moved a Shipment (P5) share: when and where it happened, and the ETA
/// if one was given, under the correlation ID of the call that reported it.
/// </summary>
/// <param name="OccurredAt">When the TMS recorded the event.</param>
/// <param name="CarrierId">The Carrier that reported the event.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for the booking, which matched the event to the Shipment.</param>
/// <param name="EventTime">When it happened, with the offset of where.</param>
public abstract record ShipmentProgressV1(
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
    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    /// <summary>Makes the event from the Shipment a Tracking event moved, taking what the event said.</summary>
    protected static TEvent From<TEvent>(Shipment shipment, TrackingEvent trackingEvent, Func<Guid, DateTimeOffset, string, string, string, string?, string?, DateTimeOffset, TrackingLocationV1, DateTimeOffset?, TEvent> create)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(trackingEvent);
        ArgumentNullException.ThrowIfNull(create);
        return create(
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
