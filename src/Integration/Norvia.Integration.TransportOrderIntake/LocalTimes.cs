using System.Globalization;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>
/// Turns a partner's local date/time into a Canonical Model timestamp (UTC, with its offset), and a Canonical Model
/// timestamp into a local date/time without an offset, as the Carrier booking format has them.
/// </summary>
internal static class LocalTimes
{
    private const string PartnerFormat = "dd-MM-yyyy HH:mm";
    private const string CanonicalFormat = "yyyy-MM-dd'T'HH:mm:ss'+00:00'";
    private const string OffsetFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private const string CarrierFormat = "yyyy-MM-dd'T'HH:mm:ss";

    public static string? ToUtc(string? localTime, Country? country)
    {
        if (country is null
            || !DateTime.TryParseExact(localTime?.Trim(), PartnerFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var utc = local - TimeZoneOf(country).GetUtcOffset(local);
        return utc.ToString(CanonicalFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The local date/time of a Canonical Model timestamp in the country's time zone, or null when it isn't one.</summary>
    public static string? FromUtc(string? timestamp, Country? country)
    {
        if (country is null || !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return null;
        }

        return TimeZoneInfo.ConvertTime(instant, TimeZoneOf(country)).DateTime.ToString(CarrierFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A Carrier format local date/time (<c>yyyy-MM-ddTHH:mm:ss</c>) as a Canonical Model timestamp with the country's
    /// offset at that time, or null when it isn't one. As in <see cref="ToUtc"/>, a time the clocks skip reads as
    /// standard time, and one they repeat as its later occurrence.
    /// </summary>
    public static string? ToLocalOffset(string? localTime, Country? country)
    {
        if (country is null
            || !DateTime.TryParseExact(localTime?.Trim(), CarrierFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var zone = TimeZoneOf(country);
        var offset = zone.IsAmbiguousTime(local) ? zone.BaseUtcOffset : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToString(OffsetFormat, CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo TimeZoneOf(Country country) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(country.TimeZoneId, out var ianaTimeZone)
            ? ianaTimeZone
            : TimeZoneInfo.FindSystemTimeZoneById(country.WindowsTimeZoneId);
}
