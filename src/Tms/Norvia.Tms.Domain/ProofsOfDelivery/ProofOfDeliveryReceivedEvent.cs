using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>A Proof of delivery was recorded for a Shipment as its current one (P6). It never changes the Shipment's status.</summary>
/// <param name="ReplacesEarlier">Whether it replaced an earlier current Proof of delivery.</param>
public sealed class ProofOfDeliveryReceivedEvent(Shipment shipment, ProofOfDelivery proofOfDelivery, bool replacesEarlier) : DomainEventBase
{
    public Shipment Shipment { get; } = shipment;

    public ProofOfDelivery ProofOfDelivery { get; } = proofOfDelivery;

    public bool ReplacesEarlier { get; } = replacesEarlier;
}
