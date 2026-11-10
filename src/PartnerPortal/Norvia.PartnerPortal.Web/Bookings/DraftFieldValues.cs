using System.Globalization;
using Norvia.PartnerPortal.Application.Bookings;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>The values of a booking by the field names of the Partner API's Booking draft (see <see cref="DraftFieldLabels"/>); an empty field is left out.</summary>
internal static class DraftFieldValues
{
    public static Dictionary<string, string> Of(BookingRequest booking)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        void Put(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[field] = value.Trim();
            }
        }

        string? Time(DateTime? time) => time?.ToString("s", CultureInfo.InvariantCulture);

        Put("bookingRef", booking.ShipperReference);
        Put("pickupStreet", booking.CollectionAddress.Street);
        Put("pickupPostalCode", booking.CollectionAddress.Postcode);
        Put("pickupCity", booking.CollectionAddress.City);
        Put("pickupCountry", booking.CollectionAddress.Country);
        Put("pickupFrom", Time(booking.CollectionWindow.From));
        Put("pickupUntil", Time(booking.CollectionWindow.Until));
        Put("receiverName", booking.Consignee.Name);
        Put("receiverContact", booking.Consignee.ContactName);
        Put("receiverPhone", booking.Consignee.Phone);
        Put("receiverEmail", booking.Consignee.Email);
        Put("receiverStreet", booking.DeliveryAddress.Street);
        Put("receiverPostalCode", booking.DeliveryAddress.Postcode);
        Put("receiverCity", booking.DeliveryAddress.City);
        Put("receiverCountry", booking.DeliveryAddress.Country);
        Put("deliverFrom", Time(booking.DeliveryWindow.From));
        Put("deliverUntil", Time(booking.DeliveryWindow.Until));
        Put("remarks", booking.Instructions);
        for (var index = 0; index < booking.GoodsLines.Count; index++)
        {
            var line = booking.GoodsLines[index];
            Put($"items.{index}.packaging", line.PackageType?.ToString());
            Put($"items.{index}.count", line.Quantity?.ToString(CultureInfo.InvariantCulture));
            Put($"items.{index}.weight", line.WeightKg?.ToString(CultureInfo.InvariantCulture));
            Put(
                $"items.{index}.dimensions",
                line.DimensionsCm is { } size ? string.Join('x', size.LengthCm, size.WidthCm, size.HeightCm) : null);
        }

        return values;
    }

    /// <summary>Whether the booking has a goods line the field names, such as <c>items.1.weight</c>.</summary>
    public static bool HasLine(BookingRequest booking, string field) =>
        field.Split('.') is ["items", var index, _] && int.TryParse(index, CultureInfo.InvariantCulture, out var line) && line < booking.GoodsLines.Count;
}
