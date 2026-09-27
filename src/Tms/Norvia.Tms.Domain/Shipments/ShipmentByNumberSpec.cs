using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

public sealed class ShipmentByNumberSpec : SingleResultSpecification<Shipment>
{
    public ShipmentByNumberSpec(ShipmentNumber number)
    {
        Number = number;
        Query.Where(shipment => shipment.Number == number).AsNoTracking();
    }

    public ShipmentNumber Number { get; }
}
