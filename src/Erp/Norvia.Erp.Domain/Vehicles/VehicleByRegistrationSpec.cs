using Ardalis.Specification;

namespace Norvia.Erp.Domain.Vehicles;

/// <summary>The Vehicle with a registration, for the rule that a registration belongs to one Vehicle.</summary>
public sealed class VehicleByRegistrationSpec : Specification<Vehicle>
{
    public VehicleByRegistrationSpec(string registration) => Query.Where(vehicle => vehicle.Registration == registration);
}
