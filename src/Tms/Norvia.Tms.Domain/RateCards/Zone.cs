using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>A named area in a Rate Card: a country plus a set of postcode prefixes.</summary>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
/// <param name="PostcodePrefixes">Compared without spaces and in upper case.</param>
public sealed record Zone(ZoneName Name, string Country, IReadOnlyList<string> PostcodePrefixes)
{
    /// <summary>
    /// How specifically the Zone covers the address: the length of its longest postcode prefix the address starts
    /// with, or null when the address lies outside the Zone. Where Zones overlap, the most specific one wins.
    /// </summary>
    public int? SpecificityFor(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Country != Country || address.Postcode is null)
        {
            return null;
        }

        var postcode = Normalize(address.Postcode);
        return NormalizedPrefixes()
            .Where(prefix => postcode.StartsWith(prefix, StringComparison.Ordinal))
            .Select(prefix => (int?)prefix.Length)
            .Max();
    }

    /// <summary>
    /// The postcode prefixes this Zone and another both claim in the same country. On such a prefix neither Zone is
    /// more specific, so a Rate Card version can't hold both.
    /// </summary>
    public IEnumerable<string> PrefixesSharedWith(Zone other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return other.Country == Country ? NormalizedPrefixes().Intersect(other.NormalizedPrefixes()) : [];
    }

    private IEnumerable<string> NormalizedPrefixes() => PostcodePrefixes.Select(Normalize);

    private static string Normalize(string postcode) => postcode.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
