using System.Globalization;

namespace Norvia.PartnerPortal.Web;

/// <summary>Shows times as the Personas, all Dutch parties, read them: in Rotterdam's time zone, whatever the server's is.</summary>
public static class PortalTime
{
    private static readonly TimeZoneInfo Rotterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Invariant, so month and day names read the same whatever ICU data the host has.
    public static string Format(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, Rotterdam).ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
