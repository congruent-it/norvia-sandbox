using Ardalis.Specification;

namespace Norvia.DriverApp.Domain.Trips;

/// <summary>A Driver's Trips, in the order of their numbers.</summary>
public sealed class TripsOfDriverSpecification : Specification<DriverTrip, DriverTripRow>
{
    public TripsOfDriverSpecification(DriverId driverId)
    {
        Query.AsNoTracking()
            .Where(trip => trip.DriverId == driverId)
            .OrderBy(trip => trip.Id);
        Query.Select(trip => new DriverTripRow(trip.Id, trip.Version, trip.DriverId, trip.Details));
    }
}
