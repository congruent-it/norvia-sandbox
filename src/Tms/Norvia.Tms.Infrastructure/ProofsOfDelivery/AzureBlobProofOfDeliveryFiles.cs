using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Infrastructure.ProofsOfDelivery;

/// <summary>
/// Keeps Proof of delivery files in the TMS's own blob container: Azurite locally, a storage account in Azure whose
/// lifecycle rule moves them to the cool tier after 30 days. A blob is named by its Shipment number and the file's ID.
/// </summary>
internal sealed class AzureBlobProofOfDeliveryFiles(BlobServiceClient blobService) : IProofOfDeliveryFiles
{
    public const string ContainerName = "proofs-of-delivery";

    private readonly BlobContainerClient _container = blobService.GetBlobContainerClient(ContainerName);

    public async Task StoreAsync(ShipmentNumber shipmentNumber, ProofOfDeliveryFile file, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipmentNumber);
        ArgumentNullException.ThrowIfNull(file);

        // Aspire provisions the container in Azure; this makes it where nothing has, such as a fresh emulator.
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var options = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType } };
        await _container.GetBlobClient(BlobNameOf(shipmentNumber, file)).UploadAsync(BinaryData.FromBytes(content), options, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(ShipmentNumber shipmentNumber, ProofOfDeliveryFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipmentNumber);
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            return await _container.GetBlobClient(BlobNameOf(shipmentNumber, file)).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken)
    {
        var deleted = 0;
        try
        {
            await foreach (var blob in _container.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if ((await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken)).Value)
                {
                    deleted++;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No container yet: no file was ever stored.
        }

        return deleted;
    }

    private static string BlobNameOf(ShipmentNumber shipmentNumber, ProofOfDeliveryFile file) => $"{shipmentNumber.Value}/{file.Id:N}";
}
