namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The Proof of delivery queue the Carrier Proof of delivery Logic App reads (ADR 0009).</summary>
internal interface IProofOfDeliveryQueue
{
    Task EnqueueAsync(ProofOfDeliveryMessage message, CancellationToken cancellationToken);
}
