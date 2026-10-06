namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>Stands in for the Proof of delivery queue: keeps every message put on it, noting which blobs were stored by then, or fails as Service Bus would when unreachable.</summary>
internal sealed class StubProofOfDeliveryQueue(StubInboundProofsOfDelivery inbound) : IProofOfDeliveryQueue
{
    public List<ProofOfDeliveryMessage> Messages { get; } = [];

    /// <summary>For each message, whether the blob it names was already stored when it was queued.</summary>
    public List<bool> BlobWasStoredFirst { get; } = [];

    public Exception? Failure { get; set; }

    public Task EnqueueAsync(ProofOfDeliveryMessage message, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Messages.Add(message);
        BlobWasStoredFirst.Add(inbound.Blobs.ContainsKey(message.MessageId));
        return Task.CompletedTask;
    }
}
