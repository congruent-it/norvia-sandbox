using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Off the road days from a date on, in order, read-only: what the Vehicles tab lists, and what the horizon job honours.</summary>
public sealed class OffTheRoadDaysFromSpec : Specification<OffTheRoadDay>
{
    public OffTheRoadDaysFromSpec(DateOnly from)
    {
        Query.Where(day => day.Date >= from).OrderBy(day => day.Date).AsNoTracking();
    }
}
