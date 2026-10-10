namespace Norvia.DriverApp.Application.Trips;

public sealed record StopGoodsLineDto(string? PackageType, int? Quantity, decimal? WeightKg, StopDimensionsDto? DimensionsCm);
