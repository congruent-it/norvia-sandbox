using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The Open days, tracked, for the horizon job to close the ones before today.</summary>
public sealed class OpenPlanningDaysToCloseSpec : Specification<PlanningDay>
{
    public OpenPlanningDaysToCloseSpec()
    {
        Query.Where(day => day.Status == PlanningDayStatus.Open).OrderBy(day => day.Date);
    }
}
