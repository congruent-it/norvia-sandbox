using Ardalis.Specification;

namespace Norvia.Tms.Domain.Carriers;

/// <summary>The replica with an ID, tracked so a replacement can change it.</summary>
public sealed class CarrierCopyForReplaceSpec : Specification<CarrierCopy>
{
    public CarrierCopyForReplaceSpec(string id)
    {
        Query.Where(copy => copy.Id == id);
    }
}
