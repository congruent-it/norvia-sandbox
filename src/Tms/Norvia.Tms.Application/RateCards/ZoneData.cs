namespace Norvia.Tms.Application.RateCards;

public sealed record ZoneData(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);
