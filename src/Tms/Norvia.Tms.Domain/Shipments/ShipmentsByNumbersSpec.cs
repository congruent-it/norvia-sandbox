using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>Shipments by their numbers, for putting several on a Trip at once (P11).</summary>
public sealed class ShipmentsByNumbersSpec : Specification<Shipment>
{
    public ShipmentsByNumbersSpec(IReadOnlyCollection<ShipmentNumber> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        Query.Where(shipment => numbers.Contains(shipment.Number)).AsNoTracking();
    }
}
