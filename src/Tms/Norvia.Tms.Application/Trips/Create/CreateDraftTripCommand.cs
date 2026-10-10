using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Trips.Create;

/// <summary>
/// The Planner creates a Draft Trip for an open day (P11), with or without its Vehicle, Driver, planned start and driving duration. P10's availability rules
/// apply to what is given, and so do the rules about the Shipments, if any: each adds its collection and delivery Stops, and the Trip is created with all of them or none. A request sent again under its idempotency key is answered with the Draft it created. Nothing is published.
/// </summary>
public sealed record CreateDraftTripCommand(
    DateOnly Day, VehicleId? VehicleId, DriverId? DriverId, IdempotencyKey IdempotencyKey, TimeOnly? PlannedStart = null, TimeSpan? PlannedDrivingDuration = null, IReadOnlyList<string>? ShipmentNumbers = null) : ICommand<Result<CreateDraftTripResult>>;
