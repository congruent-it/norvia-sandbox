using Ardalis.Specification;

namespace Norvia.Tms.Domain.Trips;

/// <summary>Every Trip of a day, Draft or Dispatched: the Vehicles and Drivers they hold have no other Trip that day.</summary>
public sealed class TripsOnDaySpec : Specification<Trip>
{
    public TripsOnDaySpec(DateOnly day)
    {
        Query.Where(trip => trip.Day == day).AsNoTracking();
    }
}
