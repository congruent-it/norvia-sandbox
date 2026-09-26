using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// A weight band within a Lane: from the next lighter Weight Break (or 0 kg) up to and including <paramref name="UpToKg"/>.
/// </summary>
public sealed record WeightBreak(decimal UpToKg, Money PricePer100Kg);
