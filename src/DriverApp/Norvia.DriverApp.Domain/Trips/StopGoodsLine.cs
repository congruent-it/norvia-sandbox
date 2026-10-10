namespace Norvia.DriverApp.Domain.Trips;

public sealed record StopGoodsLine(string? PackageType, int? Quantity, decimal? WeightKg, StopDimensions? DimensionsCm);
