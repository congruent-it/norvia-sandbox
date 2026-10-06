namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>Stands in for the inbound container: keeps every blob stored in it, or fails as storage would when unreachable.</summary>
internal sealed class StubInboundProofsOfDelivery : IInboundProofsOfDelivery
{
    public Dictionary<string, byte[]> Blobs { get; } = [];

    public Exception? Failure { get; set; }

    public Task StoreAsync(string blobName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Blobs[blobName] = content.ToArray();
        return Task.CompletedTask;
    }
}
