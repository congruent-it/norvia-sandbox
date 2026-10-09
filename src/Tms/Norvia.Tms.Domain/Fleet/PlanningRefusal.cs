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

    public static PlanningRefusal VehicleNotFound(string vehicleId) =>
        new(PlanningRefusalCode.VehicleNotFound, Invariant($"The Own fleet has no active Vehicle {vehicleId}."));

    public static PlanningRefusal DriverAbsent(string driverId, DateOnly date) =>
        new(PlanningRefusalCode.DriverAbsent, Invariant($"Driver {driverId} has an Absence on {date:yyyy-MM-dd}: remove it before editing their Shift."));

    public static PlanningRefusal AbsenceNotFound() =>
        new(PlanningRefusalCode.AbsenceNotFound, "The Driver has no such Absence.");

    public static PlanningRefusal OffTheRoadDayNotFound() =>
        new(PlanningRefusalCode.OffTheRoadDayNotFound, "The Vehicle has no such Off the road day.");

    public static PlanningRefusal PeriodInvalid(int maxDays) =>
        new(PlanningRefusalCode.PeriodInvalid, Invariant($"A period must end on or after the day it starts, and cover at most {maxDays} days."));

    public static PlanningRefusal RemarkTooLong(int maxLength) =>
        new(PlanningRefusalCode.RemarkTooLong, Invariant($"A remark can be at most {maxLength} characters."));
}
