using Ardalis.Specification;

namespace Norvia.Tms.Domain.Trips;

/// <summary>Every Trip, by day and then number, read-only: what the Trips view lists. A Draft on a day that has passed stays on it, so the Planner can discard it.</summary>
public sealed class TripsInOrderSpec : Specification<Trip>
{
    public TripsInOrderSpec()
    {
        Query.OrderBy(trip => trip.Day).ThenBy(trip => trip.Number).AsNoTracking();
    }
}
