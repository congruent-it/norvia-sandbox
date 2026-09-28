using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipment, tracked, so a planning action on it, such as a reassignment or a release, saves its changes.</summary>
public sealed class ShipmentToChangeSpec : SingleResultSpecification<Shipment>
{
    public ShipmentToChangeSpec(ShipmentNumber number)
    {
        Number = number;
        Query.Where(shipment => shipment.Number == number);
    }

    public ShipmentNumber Number { get; }
}
