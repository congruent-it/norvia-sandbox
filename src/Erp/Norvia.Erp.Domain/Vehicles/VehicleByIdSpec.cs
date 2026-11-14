using Ardalis.Specification;

namespace Norvia.Erp.Domain.Vehicles;

/// <summary>The Vehicle with an ID, which the seed loader reloads and Finance edits.</summary>
public sealed class VehicleByIdSpec : Specification<Vehicle>
{
    public VehicleByIdSpec(VehicleId id) => Query.Where(vehicle => vehicle.Id == id);
}
