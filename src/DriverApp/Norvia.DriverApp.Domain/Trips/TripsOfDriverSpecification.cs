using Ardalis.Specification;

namespace Norvia.DriverApp.Domain.Trips;

/// <summary>A Driver's Trips, in the order of their numbers.</summary>
public sealed class TripsOfDriverSpecification : Specification<DriverTrip>
{
    public TripsOfDriverSpecification(string driverId) =>
        Query.Where(trip => trip.DriverId == driverId).OrderBy(trip => trip.Id);
}
