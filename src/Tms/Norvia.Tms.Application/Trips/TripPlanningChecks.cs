using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// Applies P10's availability rules to a Trip the Planner wants to create or change. P11 owns Trips, so it tells P10
/// whether the Vehicle or Driver has another Trip that day: any Trip of that day counts, Draft or Dispatched.
/// </summary>
public sealed class TripPlanningChecks(IReadRepository<Trip> trips, TripAvailabilityRules rules)
{
    /// <param name="current">The Trip being changed, which isn't its own "other Trip".</param>
    public async Task<IReadOnlyList<TripAvailabilityFailure>> CheckAsync(
        DateOnly day, VehicleId? vehicle, DriverId? driver, Trip? current, CancellationToken cancellationToken)
    {
        var others = (await trips.ListAsync(new TripsOnDaySpec(day), cancellationToken)).Where(trip => trip != current).ToList();
        var proposal = new TripProposal(
            day,
            FleetLoad.None,
            vehicle,
            driver,
            VehicleHasOtherTrip: vehicle is { } v && others.Any(trip => trip.VehicleId == v),
            DriverHasOtherTrip: driver is { } d && others.Any(trip => trip.DriverId == d));
        return await rules.CheckAsync(proposal, cancellationToken);
    }
}
