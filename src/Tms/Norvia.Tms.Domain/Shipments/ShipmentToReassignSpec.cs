using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipment, tracked, so a reassignment saves its changes.</summary>
public sealed class ShipmentToReassignSpec : SingleResultSpecification<Shipment>
{
    public ShipmentToReassignSpec(ShipmentNumber number)
    {
        Number = number;
        Query.Where(shipment => shipment.Number == number);
    }

    public ShipmentNumber Number { get; }
}
