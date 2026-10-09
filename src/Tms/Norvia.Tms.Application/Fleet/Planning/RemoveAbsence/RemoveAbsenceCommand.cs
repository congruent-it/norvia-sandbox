using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RemoveAbsence;

/// <summary>
/// The Planner removes an Absence (P10). The Driver can work that day again, with the Shift from their Roster. An Absence on a
/// day before today is closed and stays.
/// </summary>
public sealed record RemoveAbsenceCommand(AbsenceId Id) : ICommand<Result>;
