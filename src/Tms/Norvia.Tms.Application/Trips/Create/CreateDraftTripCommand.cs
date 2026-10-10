using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Trips.Create;

/// <summary>
/// The Planner creates a Draft Trip for an open day (P11), with or without its Vehicle, Driver, planned start and driving duration. P10's availability rules
/// apply to what is given. A request sent again under its idempotency key is answered with the Draft it created. Nothing is published.
/// </summary>
public sealed record CreateDraftTripCommand(
    DateOnly Day, VehicleId? VehicleId, DriverId? DriverId, IdempotencyKey IdempotencyKey, TimeOnly? PlannedStart = null, TimeSpan? PlannedDrivingDuration = null) : ICommand<Result<CreateDraftTripResult>>;
