using System.Globalization;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>A goods line in a sentence a Carrier reads: "4 × pallet, 2400 kg, 120 × 80 × 160 cm".</summary>
public static class GoodsText
{
    public static string Of(CarrierBookingGoodsLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var parts = new List<string>();
        if (line.Quantity is { } quantity || !string.IsNullOrWhiteSpace(line.PackageType))
        {
            parts.Add($"{line.Quantity?.ToString(CultureInfo.InvariantCulture)} × {line.PackageType}".Trim(' ', '×'));
        }

        if (line.WeightKg is { } weight)
        {
            parts.Add($"{weight.ToString("0.##", CultureInfo.InvariantCulture)} kg");
        }

        if (line.LengthCm is { } length && line.WidthCm is { } width && line.HeightCm is { } height)
        {
            parts.Add(string.Join(" × ", new[] { length, width, height }.Select(size => size.ToString("0.##", CultureInfo.InvariantCulture))) + " cm");
        }

        return string.Join(", ", parts);
    }
}
