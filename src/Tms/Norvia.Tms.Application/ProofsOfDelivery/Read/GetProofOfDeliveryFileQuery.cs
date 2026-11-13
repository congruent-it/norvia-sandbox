using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

/// <summary>Reads one file of a Shipment's Proofs of delivery, current or superseded. NotFound when the Shipment or the file doesn't exist, and NotFound marked <see cref="ProofOfDeliveryFileExpiry.IsExpired"/> when the file is listed but its content is gone.</summary>
public sealed record GetProofOfDeliveryFileQuery(string ShipmentNumber, Guid FileId) : IQuery<Result<ProofOfDeliveryFileContent>>;

/// <summary>A file's content, to be streamed to the caller, who disposes it.</summary>
public sealed record ProofOfDeliveryFileContent(string Name, string ContentType, Stream Content);
