using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalProofOfDeliveryWorker;

/// <summary>The inbound container on Azurite, or on a storage account when the worker is pointed at one.</summary>
internal sealed partial class BlobInboundProofsOfDelivery(BlobServiceClient blobService, IOptions<ProofOfDeliveryWorkerOptions> options, ILogger<BlobInboundProofsOfDelivery> logger)
    : IInboundProofsOfDelivery
{
    private BlobContainerClient Container => blobService.GetBlobContainerClient(options.Value.InboundContainer);

    public async Task<string?> ReadAsync(string blobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        try
        {
            var content = await Container.GetBlobClient(blobName).DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToString();
        }
        catch (RequestFailedException exception)
        {
            LogNotRead(exception);
            return null;
        }
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        await Container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The inbound blob couldn't be read")]
    private partial void LogNotRead(Exception exception);
}
