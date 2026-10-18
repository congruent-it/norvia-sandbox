namespace Norvia.Erp.Application.MasterData.RateCards;

/// <summary>A Rate Card or a Carrier Rate Card with the party it belongs to, which is a Shipper or a Carrier.</summary>
public sealed record RateCardView(string Id, string PartyId, string PartyName, IReadOnlyList<RateCardVersionView> Versions);

public sealed record RateCardVersionView(int Version, DateOnly ValidFrom, DateOnly? ValidTo, int ZoneCount, IReadOnlyList<LaneView> Lanes);

public sealed record LaneView(string Origin, string Destination, decimal MinimumCharge, int WeightBreakCount);
