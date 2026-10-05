using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.Integration.TransportOrderIntake;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// The Carrier-tracking map (P5): turns a Carrier's tracking message in the Carrier tracking format into the Canonical
/// Model Tracking event the TMS's Tracking event API takes. The Carrier is the one API Management took from the token,
/// never one the Carrier's payload names (ADR 0005).
/// </summary>
/// <remarks>
/// The mapping table lives here, not in the TMS: a status code that isn't in it fails the run. Any other value the map
/// can't read (a country it doesn't know, a time in another format) is left out, as in the intake map, and the TMS's
/// Tracking event rules report what is then missing with their stable codes. The ETA is the one time that isn't local:
/// the message holds no delivery address to read it in, so it carries its UTC offset and leaves as UTC.
/// </remarks>
public static class CarrierTrackingMap
{
    private static readonly Dictionary<string, string> EventTypes = new(StringComparer.Ordinal)
    {
        ["PU"] = "Collected",
        ["DEP"] = "Departed",
        ["ARR"] = "Arrived",
        ["OFD"] = "OutForDelivery",
        ["POD"] = "Delivered",
        ["NPU"] = "CollectionFailed",
        ["NDL"] = "DeliveryFailed",
        ["DMG"] = "Damaged",
    };

    /// <param name="carrierTracking">The Carrier's tracking message as JSON text.</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    /// <exception cref="UnknownStatusCodeException">The status code isn't in the code list.</exception>
    public static JsonObject ToTrackingEvent(string carrierTracking, string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        var message = Parse(carrierTracking);
        var code = Text(message, "statusCode");
        if (code is null || !EventTypes.TryGetValue(code, out var type))
        {
            throw new UnknownStatusCodeException(code);
        }

        var alpha2 = Alpha3Codes.Alpha2Of(Text(message, "country"));
        var country = Countries.Find(alpha2);

        var trackingEvent = new JsonObject
        {
            ["eventId"] = Text(message, "eventId"),
            ["source"] = new JsonObject { ["type"] = "Carrier", ["carrierId"] = carrierId },
            ["carrierBookingReference"] = Text(message, "bookingRef"),
            ["carrierBookingNumber"] = Text(message, "carrierBookingNumber"),
            ["type"] = type,
            ["eventTime"] = LocalTimes.ToLocalOffset(Text(message, "eventTime"), country),
            ["location"] = WithoutNulls(new JsonObject
            {
                ["place"] = Text(message, "place"),
                ["postcode"] = Text(message, "postcode"),
                ["countryCode"] = alpha2,
            }),
            ["eta"] = ToUtc(Text(message, "eta")),
            ["remark"] = Text(message, "remark"),
        };

        return WithoutNulls(trackingEvent);
    }

    // Kind says whether the text carried an offset: without one the time is Unspecified, and no instant.
    private static string? ToUtc(string? time) =>
        DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant) && instant.Kind != DateTimeKind.Unspecified
            ? instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture)
            : null;

    private static JsonObject WithoutNulls(JsonObject node)
    {
        foreach (var name in node.Where(property => property.Value is null).Select(property => property.Key).ToList())
        {
            node.Remove(name);
        }

        return node;
    }

    private static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
