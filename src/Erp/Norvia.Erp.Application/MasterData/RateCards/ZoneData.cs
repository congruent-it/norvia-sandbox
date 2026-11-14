namespace Norvia.Erp.Application.MasterData.RateCards;

public sealed record ZoneData(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);
