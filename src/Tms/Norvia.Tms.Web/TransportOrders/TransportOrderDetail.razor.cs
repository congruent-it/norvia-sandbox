using System.Globalization;
using Norvia.Tms.Application.CanonicalModel;

namespace Norvia.Tms.Web.TransportOrders;

public sealed partial class TransportOrderDetail
{
    // A Transport Order is shown as submitted, so a field the Shipper left out says so rather than showing nothing.
    private static string Given(string? value) => string.IsNullOrWhiteSpace(value) ? NorviaTime.NotGiven : value;

    private static string FormatNumber(decimal? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? NorviaTime.NotGiven;

    private static string FormatNumber(int? value) => FormatNumber((decimal?)value);

    private static string FormatAddress(CanonicalAddress? address)
    {
        if (address is null)
        {
            return NorviaTime.NotGiven;
        }

        var place = string.Join(' ', new[] { address.Postcode, address.City }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new[] { address.Street, place, address.Country }.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return parts.Length == 0 ? NorviaTime.NotGiven : string.Join(", ", parts);
    }

    private static string FormatDimensions(CanonicalDimensions? dimensions) =>
        dimensions is null
            ? NorviaTime.NotGiven
            : string.Join(" × ", new[] { dimensions.Length, dimensions.Width, dimensions.Height }.Select(side => FormatNumber(side)));
}
