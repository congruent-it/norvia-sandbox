namespace Norvia.Integration.LocalDriverReportsWorker;

/// <summary>The Driver app's transit container for the files of a Proof of delivery, as the Driver reports Logic App reaches it: fetch the files, delete them once the TMS accepted.</summary>
internal interface IDriverProofFiles
{
    /// <returns>The files in the order the event names them, or null when any of them is gone.</returns>
    Task<IReadOnlyList<FetchedProofFile>?> FetchAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken);

    Task DeleteAsync(IReadOnlyList<ProofFileReference> files, CancellationToken cancellationToken);
}
