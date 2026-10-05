using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>The current Proofs of delivery of several Shipments, for enriching a list built from them (P6).</summary>
public sealed class CurrentProofsOfDeliveryForShipmentsSpec : Specification<ProofOfDelivery>
{
    public CurrentProofsOfDeliveryForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(proofOfDelivery => shipmentIds.Contains(proofOfDelivery.ShipmentId) && proofOfDelivery.IsCurrent).AsNoTracking();
    }
}
