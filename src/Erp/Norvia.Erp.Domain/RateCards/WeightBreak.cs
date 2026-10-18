namespace Norvia.Erp.Domain.RateCards;

/// <summary>A weight band within a Lane: from the next lighter Weight Break (or 0 kg) up to and including <paramref name="UpToKg"/>.</summary>
public sealed record WeightBreak(decimal UpToKg, decimal PricePer100Kg);
