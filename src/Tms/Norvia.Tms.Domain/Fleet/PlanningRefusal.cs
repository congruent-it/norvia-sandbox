using static System.FormattableString;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>Why the TMS refused a Planner's action on a day of the Planning horizon: a stable code and a readable message.</summary>
public sealed record PlanningRefusal(PlanningRefusalCode Code, string Message)
{
    public static PlanningRefusal DayNotFound(DateOnly date) =>
        new(PlanningRefusalCode.PlanningDayNotFound, Invariant($"The Planning horizon has no day {date:yyyy-MM-dd}."));

    public static PlanningRefusal DayClosed(DateOnly date) =>
        new(PlanningRefusalCode.DayClosed, Invariant($"{date:yyyy-MM-dd} is closed: days before today are read-only."));

    public static PlanningRefusal DriverNotFound(string driverId) =>
        new(PlanningRefusalCode.DriverNotFound, Invariant($"The Own fleet has no active Driver {driverId}."));

    public static PlanningRefusal ShiftInvalid() =>
        new(PlanningRefusalCode.ShiftInvalid, "A Shift must end after it starts.");
}
