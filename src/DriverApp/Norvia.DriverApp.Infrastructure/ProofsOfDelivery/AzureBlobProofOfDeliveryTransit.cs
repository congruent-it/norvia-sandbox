using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Norvia.DriverApp.Application.Reports.Deliver;

namespace Norvia.DriverApp.Infrastructure.ProofsOfDelivery;

/// <summary>
/// The Driver app's transit container for the files of a Proof of delivery (ADR 0009): Azurite locally, a storage account in Azure whose lifecycle rule deletes
/// anything left after <c>integrationRetentionDays</c>. The Driver reports Logic App fetches the files and deletes them once the TMS accepted the Proof of delivery.
/// </summary>
internal sealed class AzureBlobProofOfDeliveryTransit(BlobServiceClient blobService) : IProofOfDeliveryTransit
{
    /// <summary>The container's name. The estate Bicep and the Driver reports Logic App name the same one.</summary>
    public const string ContainerName = "driver-proofs-of-delivery";

    private readonly BlobContainerClient _container = blobService.GetBlobContainerClient(ContainerName);

    public async Task<string> StoreAsync(string blobName, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        // The estate makes the container in Azure; this makes it where nothing has, such as a fresh emulator.
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } };
        await _container.GetBlobClient(blobName).UploadAsync(BinaryData.FromBytes(content), options, cancellationToken);
        return blobName;
    }
}
