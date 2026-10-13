using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>The transit container on Azurite, or on a storage account when the worker is pointed at one.</summary>
internal sealed partial class BlobDriverProofFiles(BlobServiceClient blobService, IOptions<DriverReportsWorkerOptions> options, ILogger<BlobDriverProofFiles> logger) : IDriverProofFiles
{
    private BlobContainerClient Container => blobService.GetBlobContainerClient(options.Value.ProofsContainer);

    public async Task<IReadOnlyList<FetchedProofFile>?> FetchAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var fetched = new List<FetchedProofFile>();
        foreach (var file in files)
        {
            try
            {
                var content = await Container.GetBlobClient(file.BlobReference).DownloadContentAsync(cancellationToken);
                fetched.Add(new FetchedProofFile(file.Name, file.ContentType, content.Value.Content.ToArray()));
            }
            catch (RequestFailedException exception)
            {
                LogNotRead(exception, file.BlobReference);
                return null;
            }
        }

        return fetched;
    }

    public async Task DeleteAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (var file in files)
        {
            try
            {
                await Container.GetBlobClient(file.BlobReference).DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception)
            {
                // The container's lifecycle rule deletes what is left; the TMS has the Proof of delivery.
                LogNotDeleted(exception, file.BlobReference);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file {BlobReference} couldn't be read from the transit container")]
    private partial void LogNotRead(Exception exception, string blobReference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file {BlobReference} couldn't be deleted from the transit container")]
    private partial void LogNotDeleted(Exception exception, string blobReference);
}
