using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

/// <summary>Reads a Shipment's current Proof of delivery. NotFound when the Shipment doesn't exist or has none.</summary>
public sealed record GetProofOfDeliveryQuery(string ShipmentNumber) : IQuery<Result<ProofOfDeliveryView>>;
