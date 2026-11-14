namespace Norvia.Erp.Application.MasterData.RateCards;

/// <summary>One dated version of a Rate Card or a Carrier Rate Card with everything it prices: the shape of the API's body and of the events.</summary>
public sealed record RateCardVersionData(int Version, DateOnly ValidFrom, DateOnly? ValidTo, IReadOnlyList<ZoneData> Zones, IReadOnlyList<LaneData> Lanes);
