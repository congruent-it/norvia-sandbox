using Ardalis.Specification;

namespace Norvia.Erp.Domain.Shippers;

/// <summary>The Shipper with an ID, which the seed loader reloads.</summary>
public sealed class ShipperByIdSpec : Specification<Shipper>
{
    public ShipperByIdSpec(ShipperId id) => Query.Where(shipper => shipper.Id == id);
}
