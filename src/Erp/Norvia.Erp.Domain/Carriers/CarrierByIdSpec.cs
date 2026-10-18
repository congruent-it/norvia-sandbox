using Ardalis.Specification;

namespace Norvia.Erp.Domain.Carriers;

/// <summary>The Carrier with an ID, which the seed loader reloads.</summary>
public sealed class CarrierByIdSpec : Specification<Carrier>
{
    public CarrierByIdSpec(CarrierId id) => Query.Where(carrier => carrier.Id == id);
}
