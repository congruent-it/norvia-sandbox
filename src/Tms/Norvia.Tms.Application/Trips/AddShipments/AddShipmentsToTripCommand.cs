using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.AddShipments;

/// <summary>
/// The Planner adds Shipments to a Draft Trip (P11): each adds its collection Stop and its delivery Stop at the end of the sequence. Every
/// rule is checked for the Trip as it would become, and a refusal leaves the Trip as it was, so the Shipments go on together or not at all.
/// A Shipment already on the Trip stays where it is, so sending the same command again changes nothing. A Draft publishes nothing; on a Dispatched
/// Trip the Shipments added are an Amend, which raises the next version under <c>CorrelationId</c> and makes them Dispatched.
/// </summary>
public sealed record AddShipmentsToTripCommand(string Number, IReadOnlyList<string> ShipmentNumbers, string? CorrelationId = null) : ICommand<Result<AddShipmentsToTripResult>>;
