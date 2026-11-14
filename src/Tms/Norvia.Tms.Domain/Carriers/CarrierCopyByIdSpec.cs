using Ardalis.Specification;

namespace Norvia.Tms.Domain.Carriers;

/// <summary>The replica with an ID, to read.</summary>
public sealed class CarrierCopyByIdSpec : Specification<CarrierCopy>
{
    public CarrierCopyByIdSpec(string id)
    {
        Query.AsNoTracking().Where(copy => copy.Id == id);
    }
}
