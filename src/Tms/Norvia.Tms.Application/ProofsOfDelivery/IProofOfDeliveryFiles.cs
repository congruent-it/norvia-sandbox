using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery;

/// <summary>
/// The TMS's own storage for Proof of delivery files (ADR 0002). It keeps the content of each file, under the Shipment
/// number and the file's ID, apart from the database that describes it.
/// </summary>
public interface IProofOfDeliveryFiles
{
    Task StoreAsync(ShipmentNumber shipmentNumber, ProofOfDeliveryFile file, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Opens a stored file's content, or returns null when the storage has none.</summary>
    Task<Stream?> OpenReadAsync(ShipmentNumber shipmentNumber, ProofOfDeliveryFile file, CancellationToken cancellationToken);

    /// <summary>Deletes every stored file, for a reset. Returns how many it deleted.</summary>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken);
}
