using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// Applies P10's availability rules, and P11's rule that the planned start is inside the Driver's Shift, to a Trip the Planner
/// wants to create or change. P11 owns Trips, so it tells P10 whether the Vehicle or Driver has another Trip that day: any Trip
/// of that day counts, Draft or Dispatched.
/// </summary>
public sealed class TripPlanningChecks(IReadRepository<Trip> trips, IReadRepository<PlanningDay> days, TripAvailabilityRules rules)
{
    /// <param name="current">The Trip being changed, which isn't its own "other Trip", and whose planned driving the new duration replaces.</param>
    public async Task<TripPlanningCheck> CheckAsync(
        DateOnly day, VehicleId? vehicle, DriverId? driver, TimeOnly? start, TimeSpan? duration, Trip? current, CancellationToken cancellationToken)
    {
        var others = (await trips.ListAsync(new TripsOnDaySpec(day), cancellationToken)).Where(trip => trip.Number != current?.Number).ToList();
        var proposal = new TripProposal(
            day,
            FleetLoad.None,
            vehicle,
            driver,
            duration,
            VehicleHasOtherTrip: vehicle is { } v && others.Vehicle(v),
            DriverHasOtherTrip: driver is { } d && others.Driver(d),
            PlannedDriving: current?.PlannedDriving);
        var failures = await rules.CheckAsync(proposal, cancellationToken);
        return new TripPlanningCheck(failures, await CheckShiftAsync(day, driver, start, cancellationToken));
    }

    // A Driver without a Shift that day is DriverNotAvailable already, and a Trip without a Driver has no Shift to start in.
    private async Task<IReadOnlyList<TripRefusal>> CheckShiftAsync(DateOnly day, DriverId? driver, TimeOnly? start, CancellationToken cancellationToken)
    {
        if (driver is not { } driverId || start is not { } time)
        {
            return [];
        }

        var planningDay = await days.FirstOrDefaultAsync(new PlanningDayByDateSpec(day), cancellationToken);
        return planningDay?.Shifts.FirstOrDefault(shift => shift.DriverId == driverId) is { } shift && (time < shift.Start || time >= shift.End)
            ? [TripRefusal.OutsideShift(driverId.Value, time, day, shift.Start, shift.End)]
            : [];
    }
}
