namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>The inbound container the Carrier Proof of delivery Logic App fetches a claim check's blob from, and deletes it from once the TMS has it.</summary>
internal interface IInboundProofsOfDelivery
{
    /// <summary>The blob's content as text, or null when it can't be read, a missing blob included.</summary>
    Task<string?> ReadAsync(string blobName, CancellationToken cancellationToken);

    Task DeleteAsync(string blobName, CancellationToken cancellationToken);
}
