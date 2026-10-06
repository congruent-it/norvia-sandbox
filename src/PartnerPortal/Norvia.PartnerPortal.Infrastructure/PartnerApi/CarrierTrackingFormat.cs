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
    public static JsonObject ToJson(CarrierTrackingEvent trackingEvent)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        return new JsonObject
        {
            ["bookingRef"] = trackingEvent.BookingReference,
            ["carrierBookingNumber"] = trackingEvent.CarrierBookingNumber,
            ["eventId"] = trackingEvent.EventId,
            ["statusCode"] = StatusCode(trackingEvent.Type),
            ["eventTime"] = CarrierLocalTime.At(trackingEvent.EventTime, trackingEvent.Country).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["place"] = trackingEvent.Place,
            ["postcode"] = trackingEvent.Postcode,
            ["country"] = trackingEvent.Country,
        };
    }

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
