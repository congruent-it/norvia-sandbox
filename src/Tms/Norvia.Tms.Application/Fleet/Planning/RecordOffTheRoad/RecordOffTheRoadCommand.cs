using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;

/// <summary>
/// The Planner takes a Vehicle off the road for one day or several (P10), also beyond the Planning horizon: the horizon job
/// honours it when it opens the day. Always accepted, even when a Trip would use the Vehicle. Recording a day that is already
/// Off the road changes its reason and remark.
/// </summary>
public sealed record RecordOffTheRoadCommand(VehicleId VehicleId, DateOnly From, DateOnly To, OffTheRoadReason Reason, string? Remark)
    : ICommand<Result<RecordOffTheRoadResult>>;
