using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>What <see cref="CarrierTrackingMap.Map"/> made of a Carrier's tracking message: the Canonical Model event, or the status code it couldn't read.</summary>
/// <param name="TrackingEvent">The Canonical Model Tracking event; null when the status code isn't in the code list.</param>
/// <param name="UnknownStatusCode">The status code that isn't in the code list; null when the message has no readable one.</param>
public sealed record TrackingMapResult(JsonObject? TrackingEvent, string? UnknownStatusCode)
{
    public bool IsMapped => TrackingEvent is not null;

    public static TrackingMapResult Mapped(JsonObject trackingEvent) => new(trackingEvent, null);

    public static TrackingMapResult UnknownStatus(string? statusCode) => new(null, statusCode);
}
