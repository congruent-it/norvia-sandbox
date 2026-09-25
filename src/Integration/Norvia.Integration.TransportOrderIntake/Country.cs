namespace Norvia.Integration.TransportOrderIntake;

/// <param name="Code">The ISO 3166-1 alpha-2 code.</param>
/// <param name="TimeZoneId">The IANA time zone that the country's local times are in.</param>
/// <param name="WindowsTimeZoneId">
/// The same time zone as Windows names it. The intake Logic App runs on Windows, where .NET finds no IANA time zone.
/// </param>
internal sealed record Country(string Code, string TimeZoneId, string WindowsTimeZoneId);
