using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.Trips.Change;

/// <summary>
/// The Planner picks the Vehicle, Driver, planned start and planned driving duration of a Draft Trip (P11), or gives them back
/// with null. On a Dispatched Trip it is an Amend: nothing may be left out, and a change raises the next version under <c>CorrelationId</c>. P10's availability rules apply to what is given, and so does the Shift rule (<c>OutsideShift</c>); a refused change
/// leaves the Trip as it was. A Draft publishes nothing.
/// </summary>
public sealed record ChangeDraftTripCommand(string Number, VehicleId? VehicleId, DriverId? DriverId, TimeOnly? PlannedStart = null, TimeSpan? PlannedDrivingDuration = null, string? CorrelationId = null) : ICommand<Result>
{
    /// <summary>True when nothing is given, so the Trip gives everything back, which is always allowed.</summary>
    public bool GivesEverythingBack => VehicleId is null && DriverId is null && PlannedStart is null && PlannedDrivingDuration is null;
}
