namespace Norvia.Integration.LocalPartnerApi;

/// <summary>A message for the Proof of delivery queue: the blob reference as JSON, and the application properties API Management sets.</summary>
internal sealed record ProofOfDeliveryMessage(string Body, string MessageId, IReadOnlyDictionary<string, string> Properties);

/// <summary>The Proof of delivery queue the Carrier Proof of delivery Logic App reads (ADR 0009).</summary>
internal interface IProofOfDeliveryQueue
{
    Task EnqueueAsync(ProofOfDeliveryMessage message, CancellationToken cancellationToken);
}
