namespace Norvia.Tms.Application.TrackingEvents.Record;

/// <summary>Where a Tracking event happened, as the Canonical Model names it.</summary>
public sealed record TrackingLocationV1(string Place, string Postcode, string CountryCode);
