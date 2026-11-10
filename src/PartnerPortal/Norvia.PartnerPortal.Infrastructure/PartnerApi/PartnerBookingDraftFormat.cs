using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Reads the Norvia Partner API's <c>booking-draft</c> (contracts/partner-api/booking-draft.v1.schema.json) into a booking the form
/// can hold, the opposite way of <see cref="PartnerBookingFormat"/>. A value the form cannot hold (a weight in a unit it cannot read)
/// is left empty and flagged, never guessed. The quotes and the ignored text are kept for the review screen; they are not part of the booking.
/// </summary>
internal static partial class PartnerBookingDraftFormat
{
    private const string LocalDateTime = "dd-MM-yyyy HH:mm";

    public static BookingDraftAnswer Read(int statusCode, string body, string? messageId)
    {
        JsonNode? json = null;
        try
        {
            json = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // The failure below does not need the body.
        }

        if (statusCode == 200 && json is JsonObject { } document && document["booking"] is JsonObject booking)
        {
            return Draft(booking, document, messageId);
        }

        var code = json?["failureCode"]?.GetValue<string>();
        var outcome = (statusCode, code) switch
        {
            (422, "MultipleBookingsInText") => BookingDraftOutcome.MultipleBookings,
            (413, _) => BookingDraftOutcome.TooLarge,
            _ => BookingDraftOutcome.Unavailable,
        };
        return BookingDraftAnswer.Refused(outcome, messageId ?? json?["messageId"]?.GetValue<string>());
    }

    private static BookingDraftAnswer Draft(JsonObject json, JsonObject document, string? messageId)
    {
        var flags = new List<FlaggedField>();
        foreach (var field in document["missing"] as JsonArray ?? [])
        {
            if (field?["field"]?.GetValue<string>() is { } name && field["reason"]?.GetValue<string>() is { } reason)
            {
                flags.Add(new FlaggedField(name, reason, field["kind"]?.GetValue<string>() == "ambiguous"));
            }
        }

        DateTime? Time(string name) => LocalTime(json, name, flags);

        var request = new BookingRequest
        {
            ShipperReference = Text(json, "bookingRef"),
            CollectionAddress = new BookingAddress(Text(json, "pickupStreet"), Text(json, "pickupPostalCode"), Text(json, "pickupCity"), Text(json, "pickupCountry")),
            CollectionWindow = new LocalWindow(Time("pickupFrom"), Time("pickupUntil")),
            Consignee = new BookingConsignee(Text(json, "receiverName"), Text(json, "receiverContact"), Text(json, "receiverPhone"), Text(json, "receiverEmail")),
            DeliveryAddress = new BookingAddress(Text(json, "receiverStreet"), Text(json, "receiverPostalCode"), Text(json, "receiverCity"), Text(json, "receiverCountry")),
            DeliveryWindow = new LocalWindow(Time("deliverFrom"), Time("deliverUntil")),
            GoodsLines = [.. (json["items"] as JsonArray ?? []).Select((item, index) => Line(item as JsonObject ?? [], index, flags))],
            Instructions = Text(json, "remarks"),
        };
        return new BookingDraftAnswer(BookingDraftOutcome.Drafted, request, flags, messageId, Quotes(document), Ignored(document));
    }

    private static Dictionary<string, string> Quotes(JsonObject document)
    {
        var quotes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, quote) in document["quotes"] as JsonObject ?? [])
        {
            if (quote is JsonValue { } value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                quotes[field] = text;
            }
        }

        return quotes;
    }

    private static List<string> Ignored(JsonObject document) =>
        [.. (document["ignored"] as JsonArray ?? [])
            .Select(snippet => snippet is JsonValue { } value && value.TryGetValue<string>(out var text) ? text.Trim() : string.Empty)
            .Where(text => text.Length > 0)];

    private static BookingGoodsLine Line(JsonObject item, int index, List<FlaggedField> flags)
    {
        PackageType? packageType = Text(item, "packaging") switch
        {
            "PX" => PackageType.Pallet,
            "PK" => PackageType.Parcel,
            "CR" => PackageType.Crate,
            "NE" => PackageType.Loose,
            _ => null,
        };
        int? count = item["count"] is JsonValue { } value && value.TryGetValue<int>(out var number) ? number : null;
        return new BookingGoodsLine(packageType, count, Weight(item, index, flags), Dimensions(item, index, flags));
    }

    private static decimal? Weight(JsonObject item, int index, List<FlaggedField> flags)
    {
        if (Text(item, "weight") is not { } text)
        {
            return null;
        }

        var match = WeightPattern().Match(text);
        if (!match.Success)
        {
            flags.Add(new FlaggedField($"items.{index}.weight", $"The weight '{text}' could not be read.", CheckThis: true));
            return null;
        }

        var kilograms = Number(match.Groups["number"].Value) * match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "KG" => 1m,
            "T" => 1000m,
            _ => 0.45359237m,
        };
        return Math.Round(kilograms, 3);
    }

    private static PackageDimensions? Dimensions(JsonObject item, int index, List<FlaggedField> flags)
    {
        if (Text(item, "dimensions") is not { } text)
        {
            return null;
        }

        var match = DimensionsPattern().Match(text);
        if (!match.Success)
        {
            flags.Add(new FlaggedField($"items.{index}.dimensions", $"The dimensions '{text}' could not be read.", CheckThis: true));
            return null;
        }

        var toCentimetres = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "CM" => 1m,
            "M" => 100m,
            _ => 0.1m,
        };
        decimal Centimetres(string group) => Math.Round(Number(match.Groups[group].Value) * toCentimetres, 3);
        return new PackageDimensions(Centimetres("length"), Centimetres("width"), Centimetres("height"));
    }

    private static DateTime? LocalTime(JsonObject json, string name, List<FlaggedField> flags)
    {
        if (Text(json, name) is not { } text)
        {
            return null;
        }

        if (DateTime.TryParseExact(text, LocalDateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        flags.Add(new FlaggedField(name, $"The time '{text}' could not be read.", CheckThis: true));
        return null;
    }

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue { } value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static decimal Number(string value) => decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<number>[0-9]+([.,][0-9]+)?) ?(?<unit>kg|t|lbs?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeightPattern();

    [GeneratedRegex(@"^(?<length>[0-9]+([.,][0-9]+)?) ?x ?(?<width>[0-9]+([.,][0-9]+)?) ?x ?(?<height>[0-9]+([.,][0-9]+)?) ?(?<unit>cm|mm|m)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DimensionsPattern();
}
