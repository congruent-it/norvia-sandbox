using System.Globalization;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>Turns a partner's local date/time into a Canonical Model timestamp: UTC, with its offset.</summary>
internal static class LocalTimes
{
    private const string PartnerFormat = "dd-MM-yyyy HH:mm";
    private const string CanonicalFormat = "yyyy-MM-dd'T'HH:mm:ss'+00:00'";

    public static string? ToUtc(string? localTime, Country? country)
    {
        if (country is null
            || !DateTime.TryParseExact(localTime?.Trim(), PartnerFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(country.TimeZoneId);
        var utc = local - timeZone.GetUtcOffset(local);
        return utc.ToString(CanonicalFormat, CultureInfo.InvariantCulture);
    }
}
