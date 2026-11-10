using Ardalis.Result;
using Mediator;
using Norvia.Agents;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>
/// The Planner assigns one Transport Order to Own fleet or to a Carrier (P3): an Accepted Transport Order gets its one
/// Shipment, Planned, and becomes Planned itself, and <c>ShipmentPlanned</c> is published, all in one transaction. The
/// assignment starts a Message trail of its own. The assignment sent again under its key is a repeat, answered as before.
/// </summary>
/// <param name="CarrierId">The Carrier to carry the Shipment; null assigns it to Own fleet.</param>
/// <param name="AssignmentKey">The idempotency key that makes a retry of the assignment a repeat.</param>
/// <param name="CorrelationId">
/// The new Message trail the TMS starts for this assignment, as it starts in the TMS UI, not at API Management. A repeat
/// answers with the trail of the assignment it repeats.
/// </param>
/// <param name="ActingOn">The Agent run whose Suggestion the Planner acted on, which the trace then names. Null when they acted on their own.</param>
public sealed record AssignTransportOrderCommand(string TransportOrderNumber, string? CarrierId, string AssignmentKey, string CorrelationId, AgentRun? ActingOn = null)
    : ICommand<Result<AssignTransportOrderResult>>;
