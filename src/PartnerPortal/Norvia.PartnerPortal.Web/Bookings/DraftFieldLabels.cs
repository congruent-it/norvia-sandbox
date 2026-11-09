using System.Globalization;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>The form's name for a field of the Booking draft, which the Partner API names as the booking schema does.</summary>
public static class DraftFieldLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["booking"] = "Booking",
        ["bookingRef"] = "Shipper reference",
        ["pickupStreet"] = "Collection street",
        ["pickupPostalCode"] = "Collection postcode",
        ["pickupCity"] = "Collection city",
        ["pickupCountry"] = "Collection country",
        ["pickupFrom"] = "Collect from",
        ["pickupUntil"] = "Collect until",
        ["receiverName"] = "Consignee",
        ["receiverContact"] = "Contact name",
        ["receiverPhone"] = "Phone",
        ["receiverEmail"] = "Email",
        ["receiverStreet"] = "Delivery street",
        ["receiverPostalCode"] = "Delivery postcode",
        ["receiverCity"] = "Delivery city",
        ["receiverCountry"] = "Delivery country",
        ["deliverFrom"] = "Deliver from",
        ["deliverUntil"] = "Deliver until",
        ["items"] = "Goods",
        ["remarks"] = "Instructions",
    };

    private static readonly Dictionary<string, string> ItemLabels = new(StringComparer.Ordinal)
    {
        ["packaging"] = "package type",
        ["count"] = "quantity",
        ["weight"] = "weight",
        ["dimensions"] = "dimensions",
    };

    public static string Of(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var parts = field.Split('.');
        if (parts is ["items", var index, var name] && int.TryParse(index, CultureInfo.InvariantCulture, out var line))
        {
            return $"Goods line {line + 1}, {ItemLabels.GetValueOrDefault(name, name)}";
        }

        return Labels.GetValueOrDefault(field, field);
    }
}
