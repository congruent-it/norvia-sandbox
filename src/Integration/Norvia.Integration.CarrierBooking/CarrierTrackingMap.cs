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

    /// <summary>The status codes of the shared code list.</summary>
    internal static IEnumerable<string> StatusCodes => EventTypes.Keys;

    /// <param name="carrierTracking">The Carrier's tracking message as JSON text.</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    /// <exception cref="UnknownStatusCodeException">The status code isn't in the code list, which fails the local function's action and so the run.</exception>
    public static JsonObject ToTrackingEvent(string carrierTracking, string carrierId)
    {
        var result = Map(carrierTracking, carrierId);
        return result.TrackingEvent ?? throw new UnknownStatusCodeException(result.UnknownStatusCode);
    }

    /// <summary>
    /// Maps the message, answering a status code that isn't in the code list as a result instead of an exception, as an
    /// expected outcome of the Carrier's message (ADR 0008). The local worker settles by it; the workflow's local function
    /// goes through <see cref="ToTrackingEvent"/>.
    /// </summary>
    /// <param name="carrierTracking">The Carrier's tracking message as JSON text.</param>
    /// <param name="carrierId">The Carrier ID API Management took from the token.</param>
    public static TrackingMapResult Map(string carrierTracking, string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        var message = JsonFields.Parse(carrierTracking);
        var code = JsonFields.Text(message, "statusCode");
        if (code is null || !EventTypes.TryGetValue(code, out var type))
        {
            return TrackingMapResult.UnknownStatus(code);
        }

        var alpha2 = Alpha3Codes.Alpha2Of(JsonFields.Text(message, "country"));
        var country = Countries.At(Countries.Find(alpha2), JsonFields.Text(message, "postcode"));

        var trackingEvent = new JsonObject
        {
            ["eventId"] = JsonFields.Text(message, "eventId"),
            ["source"] = new JsonObject { ["type"] = "Carrier", ["carrierId"] = carrierId },
            ["carrierBookingReference"] = JsonFields.Text(message, "bookingRef"),
            ["carrierBookingNumber"] = JsonFields.Text(message, "carrierBookingNumber"),
            ["type"] = type,
            ["eventTime"] = LocalTimes.ToLocalOffset(JsonFields.Text(message, "eventTime"), country),
            ["location"] = JsonFields.WithoutNulls(new JsonObject
            {
                ["place"] = JsonFields.Text(message, "place"),
                ["postcode"] = JsonFields.Text(message, "postcode"),
                ["countryCode"] = alpha2,
            }),
            ["eta"] = ToUtc(JsonFields.Text(message, "eta")),
            ["remark"] = JsonFields.Text(message, "remark"),
        };

        return TrackingMapResult.Mapped(JsonFields.WithoutNulls(trackingEvent));
    }

    // Kind says whether the text carried an offset: without one the time is Unspecified, and no instant.
    private static string? ToUtc(string? time) =>
        DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant) && instant.Kind != DateTimeKind.Unspecified
            ? instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture)
            : null;
}
