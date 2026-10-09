using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The days that exist from a date on, in order, for the horizon job to see which are missing.</summary>
public sealed class PlanningDaysFromSpec : Specification<PlanningDay>
{
    public PlanningDaysFromSpec(DateOnly from)
    {
        Query.Where(day => day.Date >= from).OrderBy(day => day.Date).AsNoTracking();
    }
}
