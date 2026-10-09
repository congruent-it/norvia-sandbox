namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// A Driver's standard weekly working pattern: for each weekday they work, a Shift start and end; a weekday without
/// one is a day off. The horizon job copies it onto each day it opens.
/// </summary>
public sealed record Roster(DriverId DriverId, IReadOnlyDictionary<DayOfWeek, ShiftTimes> Weekdays)
{
    /// <summary>The Shift this Roster gives on a day, or none when the Driver doesn't work that weekday.</summary>
    public ShiftTimes? ShiftOn(DateOnly date) => Weekdays.GetValueOrDefault(date.DayOfWeek);
}
