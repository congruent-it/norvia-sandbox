using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RecordAbsence;

/// <summary>
/// The Planner records a whole-day Absence for a Driver, for one day or several (P10), also beyond the Planning horizon: the
/// horizon job honours it when it opens the day. Always accepted, even when a Trip would use the Driver. Recording a day that
/// already has an Absence changes its reason and remark.
/// </summary>
public sealed record RecordAbsenceCommand(DriverId DriverId, DateOnly From, DateOnly To, AbsenceReason Reason, string? Remark)
    : ICommand<Result<RecordAbsenceResult>>;
