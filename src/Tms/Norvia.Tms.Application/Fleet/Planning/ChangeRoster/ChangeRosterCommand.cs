using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.ChangeRoster;

/// <summary>
/// The Planner changes a Driver's Roster (P10). The TMS rewrites every open day that isn't edited by hand and has no
/// Absence; days edited by hand keep their Shift. A weekday missing from <paramref name="Weekdays"/> is a day off.
/// </summary>
public sealed record ChangeRosterCommand(DriverId DriverId, IReadOnlyDictionary<DayOfWeek, ShiftTimes> Weekdays) : ICommand<Result<ChangeRosterResult>>;
