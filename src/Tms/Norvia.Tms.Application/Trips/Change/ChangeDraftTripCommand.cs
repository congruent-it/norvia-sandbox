using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Trips.Change;

/// <summary>
/// The Planner picks the Vehicle and Driver of a Draft Trip (P11), or gives them back with null. P10's availability rules
/// apply to what is given, and a refused change leaves the Trip as it was. Nothing is published.
/// </summary>
public sealed record ChangeDraftTripCommand(string Number, VehicleId? VehicleId, DriverId? DriverId) : ICommand<Result>;
