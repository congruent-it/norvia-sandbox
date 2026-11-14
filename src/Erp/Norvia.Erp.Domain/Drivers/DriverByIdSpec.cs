using Ardalis.Specification;

namespace Norvia.Erp.Domain.Drivers;

/// <summary>The Driver with an ID, which the seed loader reloads and Finance edits.</summary>
public sealed class DriverByIdSpec : Specification<Driver>
{
    public DriverByIdSpec(DriverId id) => Query.Where(driver => driver.Id == id);
}
