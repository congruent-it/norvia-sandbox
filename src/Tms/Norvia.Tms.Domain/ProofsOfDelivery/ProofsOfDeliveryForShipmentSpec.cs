using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>Every Proof of delivery of a Shipment, current and superseded, in delivery-time order, the order they were recorded breaking a tie (P6).</summary>
public sealed class ProofsOfDeliveryForShipmentSpec : Specification<ProofOfDelivery>
{
    public ProofsOfDeliveryForShipmentSpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(proofOfDelivery => proofOfDelivery.ShipmentId == shipmentId)
            .OrderBy(proofOfDelivery => proofOfDelivery.DeliveryTime)
            .ThenBy(proofOfDelivery => proofOfDelivery.ReceivedAt)
            .AsNoTracking();
    }

    public ShipmentId ShipmentId { get; }
}
