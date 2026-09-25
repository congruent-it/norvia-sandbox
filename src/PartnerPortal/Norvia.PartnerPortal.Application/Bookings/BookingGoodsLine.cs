namespace Norvia.PartnerPortal.Application.Bookings;

/// <param name="WeightKg">The weight of all packages on the line together.</param>
/// <param name="DimensionsCm">The size of one package; optional.</param>
public sealed record BookingGoodsLine(PackageType? PackageType, int? Quantity, decimal? WeightKg, PackageDimensions? DimensionsCm = null);
