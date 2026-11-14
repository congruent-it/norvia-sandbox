namespace Norvia.Erp.Application.MasterData.RateCards;

/// <summary>A Carrier Rate Card whole, as the <c>CarrierRateCard</c> event carries it and the save returns it.</summary>
/// <param name="Revision">The Master-data revision: 1 as loaded from the seed file, raised by every edit.</param>
public sealed record CarrierRateCardRecord(string Id, string CarrierId, int Revision, IReadOnlyList<RateCardVersionData> Versions);
