namespace Norvia.Tms.Domain.Fleet;

/// <summary>The days an Absence or an Off the road record covers at once: one or several, first to last day inclusive.</summary>
public static class AvailabilityPeriod
{
    /// <summary>The most days one action records, so a typing mistake doesn't fill years with Absences.</summary>
    public const int MaxDays = 92;

    /// <summary>The longest remark an Absence or an Off the road day keeps.</summary>
    public const int MaxRemarkLength = 500;

    /// <summary>Why an action recording a period must be refused, or null when it may go ahead: the period, the remark, and whether it starts before today.</summary>
    public static PlanningRefusal? Refusal(DateOnly from, DateOnly to, string? remark, DateOnly today)
    {
        if (!IsValid(from, to))
        {
            return PlanningRefusal.PeriodInvalid(MaxDays);
        }

        if (remark?.Trim().Length > MaxRemarkLength)
        {
            return PlanningRefusal.RemarkTooLong(MaxRemarkLength);
        }

        return from < today ? PlanningRefusal.DayClosed(from) : null;
    }

    public static bool IsValid(DateOnly from, DateOnly to) => to >= from && to.DayNumber - from.DayNumber < MaxDays;

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays);
}
