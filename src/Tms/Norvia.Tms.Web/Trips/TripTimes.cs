using System.Globalization;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Web.Trips;

/// <summary>How the Planner types and reads a Trip's planned start (<c>07:30</c>) and driving duration (<c>6:30</c>, hours and minutes).</summary>
public static class TripTimes
{
    public const string StartFormat = "HH\\:mm";
    public const string DurationFormat = "h\\:mm";

    public static string Start(TimeOnly start) => start.ToString(StartFormat, CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan duration) => $"{(int)duration.TotalHours}:{duration.Minutes:00}";

    public static string StartOrDash(TimeOnly? start) => start is { } time ? Start(time) : "—";

    public static string DurationOrDash(TimeSpan? duration) => duration is { } time ? Duration(time) : "—";

    public static bool TryParseStart(string text, out TimeOnly start) =>
        TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out start);

    /// <summary>Hours and minutes, such as <c>6:30</c>; as <see cref="Trip.IsPlannableDuration"/> says.</summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = default;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes > 59
            || parts[1].Length != 2)
        {
            return false;
        }

        duration = new TimeSpan(hours, minutes, 0);
        return Trip.IsPlannableDuration(duration);
    }
}
