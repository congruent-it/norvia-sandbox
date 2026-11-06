using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

/// <summary>One Tracking event on a Shipment's timeline.</summary>
/// <param name="EventTime">When it happened, with the offset of where it happened.</param>
/// <param name="ReceivedAt">When the TMS recorded it, apart from when it happened.</param>
/// <param name="MovedShipment">Whether it moved the Shipment's status; an event that didn't is only logged.</param>
/// <param name="Source">Who reported it: the Carrier ID, or Own fleet.</param>
public sealed record TrackingTimelineEntry(
    TrackingEventType Type,
    DateTimeOffset EventTime,
    string Place,
    string Postcode,
    string CountryCode,
    DateTimeOffset? Eta,
    string? Remark,
    DateTimeOffset ReceivedAt,
    bool MovedShipment,
    string? Source = null)
{
    public static TrackingTimelineEntry Of(TrackingEvent trackingEvent)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        return new(
            trackingEvent.Type,
            trackingEvent.EventTime,
            trackingEvent.Location.Place,
            trackingEvent.Location.Postcode,
            trackingEvent.Location.CountryCode,
            trackingEvent.Eta,
            trackingEvent.Remark,
            trackingEvent.ReceivedAt,
            trackingEvent.MovedShipment,
            trackingEvent.Source.ToString());
    }
}
