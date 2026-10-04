namespace Norvia.Tms.Domain.TrackingEvents;

/// <summary>Where a Tracking event happened: place, postcode and ISO 3166-1 alpha-2 country, as the Canonical Model carries it.</summary>
public sealed record TrackingLocation
{
    public TrackingLocation(string place, string postcode, string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(place);
        ArgumentException.ThrowIfNullOrWhiteSpace(postcode);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        Place = place;
        Postcode = postcode;
        CountryCode = countryCode;
    }

    public string Place { get; }

    public string Postcode { get; }

    public string CountryCode { get; }
}
