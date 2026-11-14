using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>Every replica, to lay over the seed file's first load.</summary>
public sealed class VehicleCopiesSpec : Specification<VehicleCopy>
{
    public VehicleCopiesSpec()
    {
        Query.AsNoTracking();
    }
}
