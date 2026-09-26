namespace Norvia.Tms.Domain.RateCards;

/// <summary>A pair of Zones, origin to destination, that a Rate Card prices, with its Weight Breaks.</summary>
/// <param name="Origin">The name of the Zone the goods are collected in.</param>
/// <param name="Destination">The name of the Zone the goods are delivered in.</param>
public sealed record Lane(string Origin, string Destination, IReadOnlyList<WeightBreak> WeightBreaks)
{
    /// <summary>The lightest Weight Break the weight fits in, or null when it is above the highest.</summary>
    public WeightBreak? WeightBreakFor(decimal weightKg) =>
        WeightBreaks.OrderBy(weightBreak => weightBreak.UpToKg).FirstOrDefault(weightBreak => weightKg <= weightBreak.UpToKg);
}
