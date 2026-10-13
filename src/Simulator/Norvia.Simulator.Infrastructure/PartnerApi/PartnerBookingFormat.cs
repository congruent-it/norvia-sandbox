using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.Simulator.Domain.Plans;

namespace Norvia.Simulator.Infrastructure.PartnerApi;

/// <summary>
/// Writes a planned booking in the Norvia Partner API's format (contracts/partner-api/booking.v1.schema.json). A field the
/// plan left out is left out, so Norvia's answer names it. The collection window is worked out now, so a booking for
/// today is never in the past.
/// </summary>
internal static class PartnerBookingFormat
{
    private const string LocalDateTime = "dd-MM-yyyy HH:mm";

    public static JsonObject ToJson(PlannedBooking booking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var window = booking.Slot.Resolve(now);
        var json = new JsonObject();
        Add(json, "bookingRef", booking.Reference);
        AddAddress(json, "pickup", booking.Pickup);
        Add(json, "pickupFrom", window.From.ToString(LocalDateTime, CultureInfo.InvariantCulture));
        Add(json, "pickupUntil", window.Until.ToString(LocalDateTime, CultureInfo.InvariantCulture));
        Add(json, "receiverName", booking.ConsigneeName);
        Add(json, "receiverContact", booking.ConsigneeContact);
        Add(json, "receiverPhone", booking.ConsigneePhone);
        AddAddress(json, "receiver", booking.Delivery);
        if (booking.Goods.Count > 0)
        {
            json["items"] = new JsonArray([.. booking.Goods.Select(Item)]);
        }

        Add(json, "remarks", booking.Remarks);
        return json;
    }

    private static JsonObject Item(PlannedGoodsLine line)
    {
        var item = new JsonObject
        {
            ["packaging"] = PackagingCode(line.Package),
            ["count"] = line.Quantity,
            ["weight"] = $"{Number(line.WeightKg)} kg",
        };
        if (line.LengthCm is { } length && line.WidthCm is { } width && line.HeightCm is { } height)
        {
            item["dimensions"] = $"{Number(length)}x{Number(width)}x{Number(height)} cm";
        }

        return item;
    }

    /// <summary>UN/ECE Recommendation 21, as the partner format has it.</summary>
    private static string PackagingCode(PackageKind package) => package switch
    {
        PackageKind.Pallet => "PX",
        PackageKind.Parcel => "PK",
        PackageKind.Crate => "CR",
        PackageKind.Loose => "NE",
        _ => throw new ArgumentOutOfRangeException(nameof(package), package, "No packaging code for this package type."),
    };

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AddAddress(JsonObject json, string party, PlannedAddress address)
    {
        Add(json, party + "Street", address.Street);
        Add(json, party + "PostalCode", address.Postcode);
        Add(json, party + "City", address.City);
        Add(json, party + "Country", address.Country);
    }

    private static void Add(JsonObject json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json[name] = value.Trim();
        }
    }
}
