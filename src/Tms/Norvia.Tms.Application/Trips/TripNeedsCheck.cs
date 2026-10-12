using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips;

/// <summary>
/// Whether a Trip has lost its Driver or Vehicle to P10: an Absence, an Off the road day, a Roster change, or a Driver or Vehicle
/// that has become inactive. It asks P10's availability rules about the Trip's own Vehicle and Driver and keeps only the two
/// rules about their availability, so another Trip that day, the licence, the budget and the load don't count. It changes nothing.
/// Only a Trip the Planner can still amend is flagged.
/// </summary>
public sealed class TripNeedsCheck(TripAvailabilityRules rules)
{
    public async Task<TripNeeds> OfAsync(Trip trip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        if (!trip.Status.CanBeAmended() || (trip.VehicleId is null && trip.DriverId is null))
        {
            return TripNeeds.None;
        }

        var failures = await rules.CheckAsync(new TripProposal(trip.Day, FleetLoad.None, trip.VehicleId, trip.DriverId), cancellationToken);
        return new TripNeeds(
            Driver: failures.Any(failure => failure.Code == TripAvailabilityCode.DriverNotAvailable),
            Vehicle: failures.Any(failure => failure.Code == TripAvailabilityCode.VehicleNotAvailable));
    }

    public async Task<IReadOnlyDictionary<string, TripNeeds>> OfAsync(IEnumerable<Trip> trips, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trips);
        var needs = new Dictionary<string, TripNeeds>();
        foreach (var trip in trips)
        {
            needs[trip.Number.Value] = await OfAsync(trip, cancellationToken);
        }

        return needs;
    }
}
