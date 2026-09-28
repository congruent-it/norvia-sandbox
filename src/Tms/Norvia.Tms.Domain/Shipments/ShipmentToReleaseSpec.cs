using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Shipment, tracked, so a release saves its changes.</summary>
public sealed class ShipmentToReleaseSpec : SingleResultSpecification<Shipment>
{
    public ShipmentToReleaseSpec(ShipmentNumber number)
    {
        Number = number;
        Query.Where(shipment => shipment.Number == number);
    }

    public ShipmentNumber Number { get; }
}
