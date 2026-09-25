using System.Globalization;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.Bookings;

namespace Norvia.PartnerPortal.Infrastructure.PartnerApi;

/// <summary>
/// Writes a booking in the Norvia Partner API's format (contracts/partner-api/booking.v1.schema.json). A field the
/// party left empty is left out, so Norvia's answer names it.
/// </summary>
internal static class PartnerBookingFormat
{
    private const string LocalDateTime = "dd-MM-yyyy HH:mm";

    public static JsonObject ToJson(BookingRequest booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var json = new JsonObject();
        Add(json, "bookingRef", booking.ShipperReference);
        AddAddress(json, "pickup", booking.CollectionAddress);
        AddWindow(json, "pickupFrom", "pickupUntil", booking.CollectionWindow);
        Add(json, "receiverName", booking.Consignee.Name);
        Add(json, "receiverContact", booking.Consignee.ContactName);
        Add(json, "receiverPhone", booking.Consignee.Phone);
        Add(json, "receiverEmail", booking.Consignee.Email);
        AddAddress(json, "receiver", booking.DeliveryAddress);
        AddWindow(json, "deliverFrom", "deliverUntil", booking.DeliveryWindow);
        if (booking.GoodsLines.Count > 0)
        {
            json["items"] = new JsonArray([.. booking.GoodsLines.Select(Item)]);
        }

        Add(json, "remarks", booking.Instructions);
        return json;
    }

    private static JsonObject Item(BookingGoodsLine line)
    {
        var item = new JsonObject();
        if (line.PackageType is { } packageType)
        {
            item["packaging"] = PackagingCode(packageType);
        }

        if (line.Quantity is { } quantity)
        {
            item["count"] = quantity;
        }

        if (line.WeightKg is { } weight)
        {
            item["weight"] = $"{Number(weight)} kg";
        }

        if (line.DimensionsCm is { } size)
        {
            item["dimensions"] = $"{Number(size.LengthCm)}x{Number(size.WidthCm)}x{Number(size.HeightCm)} cm";
        }

        return item;
    }

    /// <summary>UN/ECE Recommendation 21, as the partner format has it.</summary>
    private static string PackagingCode(PackageType packageType) => packageType switch
    {
        PackageType.Pallet => "PX",
        PackageType.Parcel => "PK",
        PackageType.Crate => "CR",
        PackageType.Loose => "NE",
        _ => throw new ArgumentOutOfRangeException(nameof(packageType), packageType, "No packaging code for this package type."),
    };

    // A negative number is written as it is: the partner format has no sign, so Norvia answers that it can't read it.
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void AddAddress(JsonObject json, string party, BookingAddress address)
    {
        Add(json, party + "Street", address.Street);
        Add(json, party + "PostalCode", address.Postcode);
        Add(json, party + "City", address.City);
        Add(json, party + "Country", address.Country);
    }

    private static void AddWindow(JsonObject json, string from, string until, LocalWindow window)
    {
        Add(json, from, window.From?.ToString(LocalDateTime, CultureInfo.InvariantCulture));
        Add(json, until, window.Until?.ToString(LocalDateTime, CultureInfo.InvariantCulture));
    }

    private static void Add(JsonObject json, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            json[name] = value.Trim();
        }
    }
}
