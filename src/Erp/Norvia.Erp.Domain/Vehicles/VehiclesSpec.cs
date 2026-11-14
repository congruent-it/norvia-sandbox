using Ardalis.Specification;

namespace Norvia.Erp.Domain.Vehicles;

/// <summary>Every Vehicle by ID, for the master data lists.</summary>
public sealed class VehiclesSpec : Specification<Vehicle>
{
    public VehiclesSpec() => Query.AsNoTracking().OrderBy(vehicle => vehicle.Id);
}
