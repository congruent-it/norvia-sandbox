using Ardalis.Specification;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Trips from one day to another, both included, that have a Driver and a planned driving duration: the ones that use up Driving hours, Draft or Dispatched.</summary>
public sealed class TripsPlannedBetweenSpec : Specification<Trip>
{
    public TripsPlannedBetweenSpec(DateOnly first, DateOnly last)
    {
        Query.Where(trip => trip.Day >= first && trip.Day <= last && trip.DriverId != null && trip.PlannedDrivingDuration != null).AsNoTracking();
    }
}
