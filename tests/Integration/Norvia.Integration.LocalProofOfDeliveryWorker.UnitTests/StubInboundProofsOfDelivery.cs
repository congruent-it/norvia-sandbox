namespace Norvia.Integration.LocalProofOfDeliveryWorker.UnitTests;

/// <summary>Stands in for the inbound container: holds the blobs a test puts there, and notes which were deleted.</summary>
internal sealed class StubInboundProofsOfDelivery : IInboundProofsOfDelivery
{
    public Dictionary<string, string> Blobs { get; } = [];

    public List<string> Deleted { get; } = [];

    public Exception? DeleteFailure { get; set; }

    public Task<string?> ReadAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(Blobs.TryGetValue(blobName, out var content) ? content : null);

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        Deleted.Add(blobName);
        Blobs.Remove(blobName);
        return Task.CompletedTask;
    }
}
