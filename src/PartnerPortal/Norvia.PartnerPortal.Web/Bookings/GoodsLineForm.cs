using Norvia.PartnerPortal.Application.Bookings;
using PackageKind = Norvia.PartnerPortal.Application.Bookings.PackageType;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>One line of goods on the form. The package type is chosen by its name, as the select holds it.</summary>
public sealed class GoodsLineForm
{
    /// <summary>The package types, in the order the form offers them.</summary>
    public static readonly IReadOnlyList<PackageKind> PackageTypes = Enum.GetValues<PackageKind>();

    public string PackageType { get; set; } = NameOf(PackageKind.Pallet);

    public int? Quantity { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public static string NameOf(PackageKind packageType) => packageType.ToString().ToLowerInvariant();

    public static GoodsLineForm From(BookingGoodsLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new GoodsLineForm
        {
            PackageType = line.PackageType is { } packageType ? NameOf(packageType) : string.Empty,
            Quantity = line.Quantity,
            WeightKg = line.WeightKg,
            LengthCm = line.DimensionsCm?.LengthCm,
            WidthCm = line.DimensionsCm?.WidthCm,
            HeightCm = line.DimensionsCm?.HeightCm,
        };
    }

    public BookingGoodsLine ToGoodsLine() => new(
        PackageTypes.Where(packageType => NameOf(packageType) == PackageType).Select(packageType => (PackageKind?)packageType).FirstOrDefault(),
        Quantity,
        WeightKg,
        (LengthCm, WidthCm, HeightCm) is ({ } length, { } width, { } height) ? new PackageDimensions(length, width, height) : null);
}
