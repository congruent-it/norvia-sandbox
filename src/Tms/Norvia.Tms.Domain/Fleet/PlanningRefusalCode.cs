namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// Why the TMS refused a Planner's action on a day of the Planning horizon (P10). The names are stable codes that
/// callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum PlanningRefusalCode
{
    /// <summary>The Planning horizon has no such day: it is after the horizon, or the TMS never opened it.</summary>
    PlanningDayNotFound = 1,

    /// <summary>The day is before today, so it is closed and read-only.</summary>
    DayClosed,

    /// <summary>The Own fleet has no active Driver with that ID.</summary>
    DriverNotFound,

    /// <summary>A Shift ends after it starts, on the same day.</summary>
    ShiftInvalid,
}
