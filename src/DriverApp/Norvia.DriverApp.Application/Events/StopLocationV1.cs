namespace Norvia.DriverApp.Application.Events;

/// <summary>Where a Stop report happened: the Stop's address, as the Canonical Model's Tracking event wants it. The Driver app doesn't read GPS.</summary>
public sealed record StopLocationV1(string Place, string Postcode, string CountryCode);
