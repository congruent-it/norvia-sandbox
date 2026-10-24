using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// A Carrier's Tracking event in the Carrier tracking format of the Norvia Partner API
/// (contracts/partner-api/carrier-tracking.v1.schema.json). It never names the Carrier: Norvia takes it from the token
/// (ADR 0005).
/// </summary>
internal static class CarrierTrackingFormat
{
    /// <summary>The status code the unmapped-code fault sends for a Delivered event: a Carrier's own spelling, outside the mapping table.</summary>
    public const string UnmappedDeliveredCode = "DLV";

    /// <param name="unmappedCode">Whether the unmapped-code fault applies: a Delivered event then carries <see cref="UnmappedDeliveredCode"/> instead of <c>POD</c>.</param>
    public static JsonObject ToJson(CarrierTrackingEvent trackingEvent, bool unmappedCode = false)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        return new JsonObject
        {
            ["bookingRef"] = trackingEvent.BookingReference,
            ["carrierBookingNumber"] = trackingEvent.CarrierBookingNumber,
            ["eventId"] = trackingEvent.EventId,
            ["statusCode"] = unmappedCode && IsUnmappable(trackingEvent.Type) ? UnmappedDeliveredCode : StatusCode(trackingEvent.Type),
            ["eventTime"] = CarrierLocalTime.At(trackingEvent.EventTime, trackingEvent.Country).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["place"] = trackingEvent.Place,
            ["postcode"] = trackingEvent.Postcode,
            ["country"] = trackingEvent.Country,
        };
    }

    /// <summary>Whether the unmapped-code fault changes this event: only the headline incident's Delivered one.</summary>
    public static bool IsUnmappable(TrackingEventType type) => type == TrackingEventType.Delivered;

    public static string StatusCode(TrackingEventType type) => type switch
    {
        TrackingEventType.Collected => "PU",
        TrackingEventType.Departed => "DEP",
        TrackingEventType.Arrived => "ARR",
        TrackingEventType.OutForDelivery => "OFD",
        TrackingEventType.Delivered => "POD",
        TrackingEventType.CollectionFailed => "NPU",
        TrackingEventType.DeliveryFailed => "NDL",
        TrackingEventType.Damaged => "DMG",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Every Tracking event has a Carrier status code."),
    };
}
