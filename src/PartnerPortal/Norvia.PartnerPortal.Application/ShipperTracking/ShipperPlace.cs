namespace Norvia.PartnerPortal.Application.ShipperTracking;

/// <summary>A place as a Shipper sees it: city and country, either of which may be missing.</summary>
public sealed record ShipperPlace(string? City, string? Country)
{
    public override string ToString() => (City, Country) switch
    {
        (null, null) => string.Empty,
        ({ } city, null) => city,
        (null, { } country) => country,
        ({ } city, { } country) => $"{city}, {country}",
    };
}
