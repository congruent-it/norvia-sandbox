using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.Trips;

/// <summary>Whether a Vehicle or Driver is held by one of the Trips of a day: a Vehicle or Driver has at most one Trip a day, Drafts included.</summary>
public static class TripHolds
{
    public static bool Vehicle(this IEnumerable<Trip> trips, VehicleId vehicle)
    {
        ArgumentNullException.ThrowIfNull(trips);
        return trips.Any(trip => trip.VehicleId == vehicle);
    }

    public static bool Driver(this IEnumerable<Trip> trips, DriverId driver)
    {
        ArgumentNullException.ThrowIfNull(trips);
        return trips.Any(trip => trip.DriverId == driver);
    }
}
