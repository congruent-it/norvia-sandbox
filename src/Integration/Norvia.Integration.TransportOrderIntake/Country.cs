namespace Norvia.Integration.TransportOrderIntake;

/// <param name="Code">The ISO 3166-1 alpha-2 code.</param>
/// <param name="TimeZoneId">The IANA time zone that the country's local times are in.</param>
internal sealed record Country(string Code, string TimeZoneId);
