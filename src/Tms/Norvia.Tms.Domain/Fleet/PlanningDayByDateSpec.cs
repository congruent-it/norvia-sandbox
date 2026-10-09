using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

public sealed class PlanningDayByDateSpec : Specification<PlanningDay>
{
    public PlanningDayByDateSpec(DateOnly date)
    {
        Query.Where(day => day.Date == date);
    }
}
