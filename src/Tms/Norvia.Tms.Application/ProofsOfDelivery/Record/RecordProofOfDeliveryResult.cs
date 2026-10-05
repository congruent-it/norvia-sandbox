using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

/// <summary>What recording a Proof of delivery did: the Shipment it is for, its status, which is unchanged, and the Proof of delivery.</summary>
/// <param name="ProofOfDeliveryId">The recorded Proof of delivery, whose files are read back through the API.</param>
/// <param name="DocumentId">The sender's own ID for the Proof of delivery.</param>
/// <param name="NumberOfFiles">How many files it holds.</param>
/// <param name="CorrelationId">The Proof of delivery's own trail.</param>
public sealed record RecordProofOfDeliveryResult(
    string ShipmentNumber,
    ShipmentStatus ShipmentStatus,
    Guid ProofOfDeliveryId,
    string DocumentId,
    int NumberOfFiles,
    string CorrelationId)
{
    public static RecordProofOfDeliveryResult From(Shipment shipment, ProofOfDelivery proofOfDelivery) =>
        new(shipment.Number.Value, shipment.Status, proofOfDelivery.Id.Value, proofOfDelivery.DocumentId, proofOfDelivery.Files.Count, proofOfDelivery.CorrelationId.Value);
}
