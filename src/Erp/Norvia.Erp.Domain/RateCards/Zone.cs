namespace Norvia.Erp.Domain.RateCards;

/// <summary>A named area in a Rate Card: a country plus a set of postcode prefixes.</summary>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
public sealed record Zone(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);
