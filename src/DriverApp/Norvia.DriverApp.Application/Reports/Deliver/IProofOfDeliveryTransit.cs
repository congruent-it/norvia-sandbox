namespace Norvia.DriverApp.Application.Reports.Deliver;

/// <summary>
/// Where the files of a Proof of delivery wait for the Driver reports Logic App (ADR 0009): the Driver app's transit container. The Logic App fetches them,
/// hands them to the TMS and deletes them once the TMS accepted the Proof of delivery. The Driver app never reads them back.
/// </summary>
public interface IProofOfDeliveryTransit
{
    /// <returns>The blob reference the event carries: the blob's name in the transit container.</returns>
    Task<string> StoreAsync(string blobName, string contentType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
