using Ardalis.Specification;

namespace Norvia.Erp.Domain.Shippers;

/// <summary>Every Shipper by name, for the read-only master data lists.</summary>
public sealed class ShippersSpec : Specification<Shipper>
{
    public ShippersSpec() => Query.AsNoTracking().OrderBy(shipper => shipper.Name);
}
