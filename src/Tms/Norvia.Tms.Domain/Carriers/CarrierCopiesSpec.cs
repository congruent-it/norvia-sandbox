using Ardalis.Specification;

namespace Norvia.Tms.Domain.Carriers;

/// <summary>Every replica, to lay over the seed file's first load.</summary>
public sealed class CarrierCopiesSpec : Specification<CarrierCopy>
{
    public CarrierCopiesSpec()
    {
        Query.AsNoTracking();
    }
}
