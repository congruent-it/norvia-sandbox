namespace Norvia.Tms.Application.RateCards;

public sealed record LaneData(string Origin, string Destination, decimal MinimumCharge, IReadOnlyList<WeightBreakData> WeightBreaks);
