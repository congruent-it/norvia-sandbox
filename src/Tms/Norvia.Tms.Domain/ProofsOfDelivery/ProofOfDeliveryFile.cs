namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>
/// One file of a Proof of delivery, such as a signed CMR or a photo. The TMS keeps its content in its own storage,
/// under the Shipment number and the file's ID, and the Proof of delivery keeps what describes it.
/// </summary>
/// <param name="Id">The file's own ID, which names it in storage and in the API.</param>
/// <param name="Name">The file's name as the sender gave it.</param>
public sealed record ProofOfDeliveryFile(Guid Id, string Name, string ContentType, long SizeBytes)
{
    public static ProofOfDeliveryFile New(string name, string contentType, long sizeBytes) => new(Guid.CreateVersion7(), name, contentType, sizeBytes);
}
