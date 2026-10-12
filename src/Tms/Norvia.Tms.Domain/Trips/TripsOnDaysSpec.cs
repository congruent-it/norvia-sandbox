using Ardalis.Specification;

namespace Norvia.Tms.Domain.Trips;

/// <summary>Every Trip of any of the days, by day and then number, read-only: the Trips the Capacity view checks for flags.</summary>
public sealed class TripsOnDaysSpec : Specification<Trip>
{
    public TripsOnDaysSpec(IReadOnlyCollection<DateOnly> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        Query.Where(trip => days.Contains(trip.Day)).OrderBy(trip => trip.Day).ThenBy(trip => trip.Number).AsNoTracking();
    }
}
