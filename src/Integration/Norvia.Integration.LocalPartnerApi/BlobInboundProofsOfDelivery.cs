using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>The inbound container on Azurite, or on a storage account when the stand-in is pointed at one.</summary>
internal sealed class BlobInboundProofsOfDelivery(BlobServiceClient blobService, IOptions<PartnerApiOptions> options) : IInboundProofsOfDelivery
{
    public async Task StoreAsync(string blobName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        var container = blobService.GetBlobContainerClient(options.Value.InboundContainer);
        // Bicep makes the container in Azure; this makes it where nothing has, such as a fresh emulator.
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await container.GetBlobClient(blobName).UploadAsync(BinaryData.FromBytes(content), overwrite: true, cancellationToken);
    }
}
