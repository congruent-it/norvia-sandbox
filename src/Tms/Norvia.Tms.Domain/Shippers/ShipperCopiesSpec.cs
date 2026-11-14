using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shippers;

/// <summary>Every replica, to lay over the seed file's first load.</summary>
public sealed class ShipperCopiesSpec : Specification<ShipperCopy>
{
    public ShipperCopiesSpec()
    {
        Query.AsNoTracking();
    }
}
