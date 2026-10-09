namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Planning horizon (P10): today and the next days, counted in Norvia's local time, with the length from the TMS settings.</summary>
public sealed class PlanningHorizonRules(int days, TimeZoneInfo zone)
{
    /// <summary>The day it is now where Norvia works.</summary>
    public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    /// <summary>The days of the Planning horizon from today, in order.</summary>
    public IEnumerable<DateOnly> DaysFrom(DateOnly today) =>
        Enumerable.Range(0, days + 1).Select(offset => today.AddDays(offset));
}
