using Ardalis.Specification;

namespace Norvia.Erp.Domain.Drivers;

/// <summary>Every Driver by name, for the master data lists.</summary>
public sealed class DriversSpec : Specification<Driver>
{
    public DriversSpec() => Query.AsNoTracking().OrderBy(driver => driver.Name);
}
