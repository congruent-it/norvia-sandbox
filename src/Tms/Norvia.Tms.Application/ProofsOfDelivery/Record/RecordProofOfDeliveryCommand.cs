using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Application.ProofsOfDelivery.Record;

/// <summary>
/// Records a Proof of delivery a Carrier sent for a Shipment (P6), in the Canonical Model, with its files: the Proof of
/// delivery becomes the Shipment's current one, its files are kept in the TMS's own storage, and ProofOfDeliveryReceived
/// is published in the same transaction as the record. It never changes the Shipment's status. The Shipment is the one on
/// the Carrier's Confirmed booking with the Carrier booking reference. The source is the Carrier, which API Management
/// sets from the token (ADR 0005), never the Carrier's own payload.
/// </summary>
/// <param name="Report">What was sent, apart from the files' content.</param>
/// <param name="Files">The files with their content, in the order of the report's own file list.</param>
/// <param name="CorrelationId">The Proof of delivery's own Message trail, which API Management creates.</param>
public sealed record RecordProofOfDeliveryCommand(
    ProofOfDeliveryReport Report,
    IReadOnlyList<ReadOnlyMemory<byte>> Files,
    string CorrelationId)
    : ICommand<Result<RecordProofOfDeliveryResult>>;
