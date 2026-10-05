using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>A Shipment's current Proof of delivery, tracked, so that a change to it is saved.</summary>
public sealed class TrackedCurrentProofOfDeliverySpec : SingleResultSpecification<ProofOfDelivery>
{
    public TrackedCurrentProofOfDeliverySpec(ShipmentId shipmentId)
    {
        ShipmentId = shipmentId;
        Query.Where(proofOfDelivery => proofOfDelivery.ShipmentId == shipmentId && proofOfDelivery.IsCurrent);
    }

    public ShipmentId ShipmentId { get; }
}
