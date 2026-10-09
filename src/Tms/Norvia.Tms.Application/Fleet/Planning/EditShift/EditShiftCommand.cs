using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.EditShift;

/// <summary>
/// The Planner sets one Driver's Shift on a single open day, for example to start early (P10). The day is then marked as edited by
/// hand. Days before today are closed and refuse it.
/// </summary>
public sealed record EditShiftCommand(DateOnly Date, DriverId DriverId, TimeOnly Start, TimeOnly End) : ICommand<Result<EditShiftResult>>;
