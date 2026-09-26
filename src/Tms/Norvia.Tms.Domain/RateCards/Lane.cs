using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>A pair of Zones, origin to destination, that a Rate Card prices, with its Weight Breaks.</summary>
/// <param name="Origin">The Zone the goods are collected in.</param>
/// <param name="Destination">The Zone the goods are delivered in.</param>
/// <param name="MinimumCharge">The least the Lane charges for a Transport Order, however light.</param>
public sealed record Lane(ZoneName Origin, ZoneName Destination, IReadOnlyList<WeightBreak> WeightBreaks, Money MinimumCharge)
{
    /// <summary>
    /// The lightest Weight Break the weight fits in, or null when it is above the highest. A weight exactly on a
    /// boundary falls in the lighter band, the one whose upper limit it equals.
    /// </summary>
    public WeightBreak? WeightBreakFor(decimal weightKg) =>
        WeightBreaks.OrderBy(weightBreak => weightBreak.UpToKg).FirstOrDefault(weightBreak => weightKg <= weightBreak.UpToKg);

    /// <summary>
    /// The charge lines for a weight, each rounded to cents: the Freight line, plus a Minimum charge adjustment line
    /// that makes up the difference when the rounded Freight is below the minimum charge. Null when the weight is above
    /// the highest Weight Break.
    /// </summary>
    public IReadOnlyList<ChargeLine>? ChargeLinesFor(decimal weightKg)
    {
        if (WeightBreakFor(weightKg) is not { } weightBreak)
        {
            return null;
        }

        var freight = new ChargeLine(ChargeLineType.Freight, weightBreak.PricePer100Kg.Times(weightKg / 100m).RoundedToCents());
        if (!freight.Amount.IsBelow(MinimumCharge))
        {
            return [freight];
        }

        return [freight, new ChargeLine(ChargeLineType.MinimumChargeAdjustment, MinimumCharge.Minus(freight.Amount).RoundedToCents())];
    }
}
