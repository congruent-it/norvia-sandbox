using Ardalis.Specification;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>Those of the Delivered Shipments that carry out one of these Transport Orders (P9).</summary>
public sealed class DeliveredShipmentsOfOrdersSpec : Specification<Shipment, DeliveredShipmentOfOrderRow>
{
    public DeliveredShipmentsOfOrdersSpec(IReadOnlyCollection<TransportOrderNumber> orderNumbers)
    {
        ArgumentNullException.ThrowIfNull(orderNumbers);
        Query.Where(shipment => shipment.Status == ShipmentStatus.Delivered && orderNumbers.Contains(shipment.TransportOrderNumber))
            .Select(shipment => new DeliveredShipmentOfOrderRow(shipment.Id, shipment.Number, shipment.TransportOrderNumber));
    }
}
