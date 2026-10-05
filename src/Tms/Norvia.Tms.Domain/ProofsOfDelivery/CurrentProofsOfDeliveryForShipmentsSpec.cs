using Ardalis.Specification;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>The IDs of those of several Shipments that have a current Proof of delivery, for enriching a list built from them (P6).</summary>
public sealed class CurrentProofsOfDeliveryForShipmentsSpec : Specification<ProofOfDelivery, ShipmentId>
{
    public CurrentProofsOfDeliveryForShipmentsSpec(IReadOnlyCollection<ShipmentId> shipmentIds)
    {
        ArgumentNullException.ThrowIfNull(shipmentIds);
        Query.Where(proofOfDelivery => shipmentIds.Contains(proofOfDelivery.ShipmentId) && proofOfDelivery.IsCurrent)
            .Select(proofOfDelivery => proofOfDelivery.ShipmentId);
    }
}
