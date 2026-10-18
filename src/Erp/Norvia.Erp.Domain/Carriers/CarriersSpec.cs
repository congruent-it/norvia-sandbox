using Ardalis.Specification;

namespace Norvia.Erp.Domain.Carriers;

/// <summary>Every Carrier by name, for the read-only master data lists.</summary>
public sealed class CarriersSpec : Specification<Carrier>
{
    public CarriersSpec() => Query.AsNoTracking().OrderBy(carrier => carrier.Name);
}
