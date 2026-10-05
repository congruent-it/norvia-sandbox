using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

/// <summary>Reads one file of a Shipment's current Proof of delivery. NotFound when the Shipment, its Proof of delivery or the file doesn't exist.</summary>
public sealed record GetProofOfDeliveryFileQuery(string ShipmentNumber, Guid FileId) : IQuery<Result<ProofOfDeliveryFileContent>>;

/// <summary>A file's content, to be streamed to the caller, who disposes it.</summary>
public sealed record ProofOfDeliveryFileContent(string Name, string ContentType, Stream Content);
