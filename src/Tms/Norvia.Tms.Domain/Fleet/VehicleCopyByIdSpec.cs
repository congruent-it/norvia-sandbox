using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The replica with an ID, to read.</summary>
public sealed class VehicleCopyByIdSpec : Specification<VehicleCopy>
{
    public VehicleCopyByIdSpec(string id)
    {
        Query.AsNoTracking().Where(copy => copy.Id == id);
    }
}
