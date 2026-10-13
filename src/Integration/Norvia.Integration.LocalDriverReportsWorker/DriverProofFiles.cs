using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>A file of a Proof of delivery as the event names it: the claim check of ADR 0009.</summary>
internal sealed record ProofFileReference(string Name, string ContentType, string BlobReference);

/// <summary>A file fetched from the transit container, ready to be a part of the TMS's multipart call.</summary>
internal sealed record FetchedProofFile(string Name, string ContentType, byte[] Content);

/// <summary>The Driver app's transit container for the files of a Proof of delivery, as the Driver reports Logic App reaches it: fetch the files, delete them once the TMS accepted.</summary>
internal interface IDriverProofFiles
{
    /// <returns>The files in the order the event names them, or null when any of them is gone.</returns>
    Task<IReadOnlyList<FetchedProofFile>?> FetchAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken);
}

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
