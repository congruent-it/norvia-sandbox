using System.Globalization;

namespace Norvia.IntegrationConsole.Web.Trails;

/// <summary>
/// Shows times as Norvia's staff read them: in Rotterdam's time zone, whatever the server's is. To the second, because
/// a trail's hops are often seconds apart.
/// </summary>
public static class ConsoleTime
{
    private static readonly TimeZoneInfo Rotterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    // Invariant, so month and day names read the same whatever ICU data the host has.
    public static string Format(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, Rotterdam).ToString("ddd d MMM yyyy, HH:mm:ss", CultureInfo.InvariantCulture);
}
