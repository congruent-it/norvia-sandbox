namespace Norvia.Erp.Application.MasterData.RateCards;

public sealed record LaneData(string Origin, string Destination, decimal MinimumCharge, IReadOnlyList<WeightBreakData> WeightBreaks);
