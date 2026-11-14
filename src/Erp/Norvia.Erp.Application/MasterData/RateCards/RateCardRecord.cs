namespace Norvia.Erp.Application.MasterData.RateCards;

/// <summary>A Rate Card whole, as the <c>RateCard</c> event carries it and the save returns it.</summary>
/// <param name="Revision">The Master-data revision: 1 as loaded from the seed file, raised by every edit.</param>
public sealed record RateCardRecord(string Id, string ShipperId, int Revision, IReadOnlyList<RateCardVersionData> Versions);
