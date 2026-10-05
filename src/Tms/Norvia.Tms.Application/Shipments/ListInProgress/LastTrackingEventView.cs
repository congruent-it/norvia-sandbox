using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.Shipments.ListInProgress;

/// <summary>What a Shipment's last Tracking event said: what happened, when, and where.</summary>
public sealed record LastTrackingEventView(TrackingEventType Type, DateTimeOffset EventTime, string Place, string Postcode, string CountryCode)
{
    public static LastTrackingEventView Of(TrackingEvent trackingEvent)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        return new(trackingEvent.Type, trackingEvent.EventTime, trackingEvent.Location.Place, trackingEvent.Location.Postcode, trackingEvent.Location.CountryCode);
    }
}
