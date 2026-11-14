using Ardalis.Specification;

namespace Norvia.Tms.Domain.Fleet;

/// <summary>The replica with an ID, tracked so a replacement can change it.</summary>
public sealed class VehicleCopyForReplaceSpec : Specification<VehicleCopy>
{
    public VehicleCopyForReplaceSpec(string id)
    {
        Query.Where(copy => copy.Id == id);
    }
}
