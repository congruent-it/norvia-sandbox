namespace Norvia.Simulator.Domain.Plans;

/// <summary>One kind of package. The weight is that of all its packages together; dimensions are those of one package, in centimetres.</summary>
public sealed record PlannedGoodsLine(PackageKind Package, int Quantity, decimal WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);
