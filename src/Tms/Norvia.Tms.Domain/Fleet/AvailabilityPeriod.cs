namespace Norvia.Tms.Domain.Fleet;

/// <summary>The days an Absence or an Off the road record covers at once: one or several, first to last day inclusive.</summary>
public static class AvailabilityPeriod
{
    /// <summary>The most days one action records, so a typing mistake doesn't fill years with Absences.</summary>
    public const int MaxDays = 92;

    public static bool IsValid(DateOnly from, DateOnly to) => to >= from && to.DayNumber - from.DayNumber < MaxDays;

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays);
}
