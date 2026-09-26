namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// A weight band within a Lane: from the next lighter Weight Break (or 0 kg) up to and including <paramref name="UpToKg"/>.
/// </summary>
/// <param name="PricePer100Kg">In EUR without VAT.</param>
public sealed record WeightBreak(decimal UpToKg, decimal PricePer100Kg);
