using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Trips.Create;

/// <summary>
/// The Planner creates a Draft Trip for an open day (P11), with or without its Vehicle and Driver. P10's availability rules
/// apply to what is given. Nothing is published.
/// </summary>
public sealed record CreateDraftTripCommand(DateOnly Day, VehicleId? VehicleId, DriverId? DriverId) : ICommand<Result<CreateDraftTripResult>>;
