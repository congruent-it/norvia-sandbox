namespace Norvia.Simulator.Domain.Worlds;

/// <summary>A Zone of a Shipper's Rate Card: a place by country and postcode prefixes (P2).</summary>
public sealed record RateZone(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);
