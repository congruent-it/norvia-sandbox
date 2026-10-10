using Ardalis.Specification;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Trips of today and later, by day and then number, read-only: what the Trips view lists.</summary>
public sealed class TripsFromSpec : Specification<Trip>
{
    public TripsFromSpec(DateOnly first)
    {
        Query.Where(trip => trip.Day >= first).OrderBy(trip => trip.Day).ThenBy(trip => trip.Number).AsNoTracking();
    }
}
