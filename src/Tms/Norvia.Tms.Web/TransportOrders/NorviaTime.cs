using System.Globalization;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Shows times as Norvia's staff read them: in Rotterdam's time zone, whatever the server's is. The Canonical Model
/// carries UTC timestamps with offsets.
/// </summary>
public static class NorviaTime
{
    public const string NotGiven = "Not given";

    private static readonly TimeZoneInfo Rotterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    // Invariant, so month and day names read the same whatever ICU data the host has.
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Format(DateTimeOffset? moment) =>
        moment is { } value ? FormatLocal(ToRotterdam(value)) : NotGiven;

    public static string FormatWindow(DateTimeOffset? earliest, DateTimeOffset? latest)
    {
        switch (earliest, latest)
        {
            case (null, null):
                return NotGiven;
            case ({ } from, null):
                return $"From {Format(from)}";
            case (null, { } until):
                return $"Until {Format(until)}";
        }

        var start = ToRotterdam(earliest.Value);
        var end = ToRotterdam(latest.Value);
        return start.Date == end.Date
            ? $"{FormatLocal(start)}–{end.ToString("HH:mm", Culture)}"
            : $"{FormatLocal(start)} – {FormatLocal(end)}";
    }

    private static DateTimeOffset ToRotterdam(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, Rotterdam);

    private static string FormatLocal(DateTimeOffset local) => local.ToString("ddd d MMM yyyy, HH:mm", Culture);
}
