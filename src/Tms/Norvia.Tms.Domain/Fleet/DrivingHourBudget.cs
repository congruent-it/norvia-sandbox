namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// A Driver's Driving-hour budget (P10): the limits less the planned driving durations of their Trips. It takes the day it
/// is and the durations as inputs, so it holds before P11 supplies any Trips.
/// </summary>
public static class DrivingHourBudget
{
    /// <summary>The days the budget looks at: from the Monday of the previous week to the Sunday of the week that holds <paramref name="today"/>.</summary>
    public static (DateOnly First, DateOnly Last) Window(DateOnly today)
    {
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (weekStart.AddDays(-7), weekStart.AddDays(6));
    }

    /// <param name="durations">The planned driving of one Driver; days outside the fortnight are ignored.</param>
    /// <param name="today">The day it is in Norvia's time zone. The week is the fixed week Monday to Sunday that holds it.</param>
    public static DrivingHoursLeft Left(IEnumerable<PlannedDriving> durations, DateOnly today, DrivingHourLimits limits)
    {
        ArgumentNullException.ThrowIfNull(durations);
        ArgumentNullException.ThrowIfNull(limits);

        var (fortnightStart, weekEnd) = Window(today);
        var weekStart = fortnightStart.AddDays(7);

        var perDay = durations
            .Where(driving => driving.Day >= fortnightStart && driving.Day <= weekEnd)
            .GroupBy(driving => driving.Day)
            .ToDictionary(group => group.Key, group => group.Aggregate(TimeSpan.Zero, (sum, driving) => sum + driving.Duration));

        var thisWeek = perDay.Where(day => day.Key >= weekStart).ToList();
        var extensionsUsed = thisWeek.Count(day => day.Value > limits.Daily);
        var usedElsewhere = thisWeek.Count(day => day.Key != today && day.Value > limits.Daily);
        var drivenToday = perDay.GetValueOrDefault(today);

        // A day over the daily limit uses an extension, so it is measured against the extended limit, but only if one is left for it.
        var dailyLimit = drivenToday > limits.Daily && usedElsewhere < limits.ExtensionsPerWeek ? limits.ExtendedDaily : limits.Daily;

        return new DrivingHoursLeft(
            Remaining(dailyLimit, drivenToday),
            Remaining(limits.Weekly, thisWeek.Aggregate(TimeSpan.Zero, (sum, day) => sum + day.Value)),
            Remaining(limits.Fortnightly, perDay.Values.Aggregate(TimeSpan.Zero, (sum, driven) => sum + driven)),
            Math.Max(0, limits.ExtensionsPerWeek - extensionsUsed));
    }

    private static TimeSpan Remaining(TimeSpan limit, TimeSpan driven) => driven >= limit ? TimeSpan.Zero : limit - driven;
}
