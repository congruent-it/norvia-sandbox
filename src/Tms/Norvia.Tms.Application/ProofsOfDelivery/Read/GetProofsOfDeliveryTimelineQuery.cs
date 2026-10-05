using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

/// <summary>Reads every Proof of delivery of a Shipment, current and superseded, for its timeline. NotFound when the Shipment doesn't exist.</summary>
public sealed record GetProofsOfDeliveryTimelineQuery(string ShipmentNumber) : IQuery<Result<IReadOnlyList<ProofOfDeliveryView>>>;
