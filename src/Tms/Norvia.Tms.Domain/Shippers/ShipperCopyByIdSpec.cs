using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shippers;

/// <summary>The replica with an ID, to read.</summary>
public sealed class ShipperCopyByIdSpec : Specification<ShipperCopy>
{
    public ShipperCopyByIdSpec(string id)
    {
        Query.AsNoTracking().Where(copy => copy.Id == id);
    }
}
