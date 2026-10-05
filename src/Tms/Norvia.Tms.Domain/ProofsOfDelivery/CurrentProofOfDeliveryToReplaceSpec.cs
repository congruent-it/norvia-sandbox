using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>A Shipment's current Proof of delivery, tracked, so that a later one can supersede it.</summary>
public sealed class CurrentProofOfDeliveryToReplaceSpec : SingleResultSpecification<ProofOfDelivery>
{
    public CurrentProofOfDeliveryToReplaceSpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(proofOfDelivery => proofOfDelivery.ShipmentId == shipmentId && proofOfDelivery.IsCurrent);
    }

    public ShipmentId ShipmentId { get; }
}
