using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>A Shipment's current Proof of delivery, which a Planner reads back.</summary>
public sealed class CurrentProofOfDeliverySpec : SingleResultSpecification<ProofOfDelivery>
{
    public CurrentProofOfDeliverySpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(proofOfDelivery => proofOfDelivery.ShipmentId == shipmentId && proofOfDelivery.IsCurrent).AsNoTracking();
    }

    public ShipmentId ShipmentId { get; }
}
