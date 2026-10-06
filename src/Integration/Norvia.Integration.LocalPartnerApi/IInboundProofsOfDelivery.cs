namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The inbound container API Management stores a Carrier's Proof of delivery in, the claim check's transit (ADR 0009).</summary>
internal interface IInboundProofsOfDelivery
{
    /// <summary>Stores the Proof of delivery as received, as the blob with this name.</summary>
    Task StoreAsync(string blobName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
