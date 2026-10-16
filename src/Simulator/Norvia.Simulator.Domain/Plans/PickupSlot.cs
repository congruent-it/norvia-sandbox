namespace Norvia.Simulator.Domain.Plans;

/// <summary>
/// When a booking is collected, as a day and an hour rather than a clock time, so the plan depends on the seed alone. The
/// window is worked out when the booking is sent, so a booking for today is never in the past (P1).
/// </summary>
public sealed record PickupSlot(int DayOffset, int FromHour, int Hours)
{
    private const string Zone = "Europe/Amsterdam";
    private const int LastStartHour = 18;

    /// <summary>Today, in Norvia's time: the day a Trip is started on.</summary>
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(Zone)).DateTime);

    /// <summary>The window at <paramref name="now"/>. Today's slot starts no earlier than the next full hour; when none is left it moves to tomorrow.</summary>
    public CollectionWindow Resolve(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(Zone)).DateTime;
        var date = local.Date.AddDays(DayOffset);
        var hour = FromHour;
        if (DayOffset == 0)
        {
            hour = Math.Max(FromHour, local.Hour + 1);
            if (hour > LastStartHour)
            {
                date = date.AddDays(1);
                hour = FromHour;
            }
        }

        var from = date.AddHours(hour);
        return new CollectionWindow(from, from.AddHours(Hours));
    }
}
