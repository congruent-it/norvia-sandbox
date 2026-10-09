using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Open days, in order: what the Capacity view lists (read-only), and what the horizon job closes once they are past.</summary>
public sealed class OpenPlanningDaysSpec : Specification<PlanningDay>
{
    public OpenPlanningDaysSpec(bool readOnly = false)
    {
        Query.Where(day => day.Status == PlanningDayStatus.Open).OrderBy(day => day.Date);
        if (readOnly)
        {
            Query.AsNoTracking();
        }
    }
}
