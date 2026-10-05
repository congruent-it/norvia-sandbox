using Ardalis.Specification;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>What the list of Delivered Shipments without a Proof of delivery needs of a Delivered Shipment (P6).</summary>
public sealed record DeliveredShipmentRow(ShipmentId Id, ShipmentNumber Number, CarriedBy CarriedBy);

/// <summary>The Delivered Shipments (P6), for finding the ones still without a Proof of delivery.</summary>
public sealed class DeliveredShipmentsSpec : Specification<Shipment, DeliveredShipmentRow>
{
    public DeliveredShipmentsSpec()
    {
        Query.Where(shipment => shipment.Status == ShipmentStatus.Delivered)
            .Select(shipment => new DeliveredShipmentRow(shipment.Id, shipment.Number, shipment.CarriedBy));
    }
}
