using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>A named area in a Rate Card: a country plus a set of postcode prefixes.</summary>
/// <param name="Country">ISO 3166-1 alpha-2 country code.</param>
public sealed record Zone(ZoneName Name, string Country, IReadOnlyList<string> PostcodePrefixes)
{
    /// <summary>Whether the address lies in the Zone. Postcodes are compared without spaces and in upper case.</summary>
    public bool Covers(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Country != Country || address.Postcode is null)
        {
            return false;
        }

        var postcode = Normalize(address.Postcode);
        return PostcodePrefixes.Any(prefix => postcode.StartsWith(Normalize(prefix), StringComparison.Ordinal));
    }

    private static string Normalize(string postcode) => postcode.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
