using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

public sealed class OffTheRoadDayByIdSpec : Specification<OffTheRoadDay>
{
    public OffTheRoadDayByIdSpec(OffTheRoadDayId id)
    {
        Query.Where(day => day.Id == id);
    }
}
