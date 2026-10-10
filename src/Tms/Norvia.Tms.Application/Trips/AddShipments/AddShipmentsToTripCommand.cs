using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Trips.AddShipments;

/// <summary>
/// The Planner adds Shipments to a Draft Trip (P11): each adds its collection Stop and its delivery Stop at the end of the sequence. Every
/// rule is checked for the Trip as it would become, and a refusal leaves the Trip as it was, so the Shipments go on together or not at all.
/// A Shipment already on the Trip stays where it is, so sending the same command again changes nothing. Nothing is published.
/// </summary>
public sealed record AddShipmentsToTripCommand(string Number, IReadOnlyList<string> ShipmentNumbers) : ICommand<Result<AddShipmentsToTripResult>>;
