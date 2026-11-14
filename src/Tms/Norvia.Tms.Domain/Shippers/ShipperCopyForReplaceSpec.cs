using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shippers;

/// <summary>The replica with an ID, tracked so a replacement can change it.</summary>
public sealed class ShipperCopyForReplaceSpec : Specification<ShipperCopy>
{
    public ShipperCopyForReplaceSpec(string id)
    {
        Query.Where(copy => copy.Id == id);
    }
}
