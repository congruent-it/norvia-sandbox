namespace Norvia.Erp.Domain.RateCards;

/// <summary>A pair of Zones, origin to destination, that a Rate Card prices, with its Weight Breaks.</summary>
/// <param name="MinimumCharge">The least the Lane charges for a Transport Order, however light.</param>
public sealed record Lane(string Origin, string Destination, decimal MinimumCharge, IReadOnlyList<WeightBreak> WeightBreaks);
