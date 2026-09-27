using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>
/// The Planner assigns one Transport Order to Own fleet or to a Carrier (P3): an Accepted Transport Order gets its one
/// Shipment, Planned, and becomes Planned itself, and <c>ShipmentPlanned</c> is published, all in one transaction. The
/// assignment starts a Message trail of its own. The assignment sent again under its key is a repeat, answered as before.
/// </summary>
/// <param name="CarrierId">The Carrier to carry the Shipment; null assigns it to Own fleet.</param>
/// <param name="AssignmentKey">The idempotency key that makes a retry of the assignment a repeat.</param>
public sealed record AssignTransportOrderCommand(string TransportOrderNumber, string? CarrierId, string AssignmentKey)
    : ICommand<Result<AssignTransportOrderResult>>;
