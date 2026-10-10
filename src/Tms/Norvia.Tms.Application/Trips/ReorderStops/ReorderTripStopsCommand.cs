using Ardalis.Result;
using Mediator;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReorderStops;

/// <summary>
/// The Planner puts the Stops of a Draft Trip in a new order (P11). The order names every Stop of the Trip once. A Shipment's delivery before its
/// collection is refused with CollectionBeforeDelivery, and a refusal leaves the order as it was. Nothing is published.
/// </summary>
public sealed record ReorderTripStopsCommand(string Number, IReadOnlyList<StopReference> Stops) : ICommand<Result>;

/// <summary>A Stop as the Planner names it: a Shipment's collection or its delivery. Two Stops at the same address are still two Stops.</summary>
public sealed record StopReference(string ShipmentNumber, StopKind Kind);
