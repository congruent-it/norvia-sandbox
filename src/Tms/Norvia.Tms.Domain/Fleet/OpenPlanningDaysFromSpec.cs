using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Open days from a date on, tracked, for an action that changes them.</summary>
public sealed class OpenPlanningDaysFromSpec : Specification<PlanningDay>
{
    public OpenPlanningDaysFromSpec(DateOnly from)
    {
        Query.Where(day => day.Status == PlanningDayStatus.Open && day.Date >= from);
    }
}
