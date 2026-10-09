using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Open days, in order, read-only: what the Capacity view lists.</summary>
public sealed class OpenPlanningDaysSpec : Specification<PlanningDay>
{
    public OpenPlanningDaysSpec()
    {
        Query.Where(day => day.Status == PlanningDayStatus.Open).OrderBy(day => day.Date).AsNoTracking();
    }
}
