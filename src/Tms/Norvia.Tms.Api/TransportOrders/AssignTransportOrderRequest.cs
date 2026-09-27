using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>Who the Planner assigns a Transport Order to: Own fleet, or the Carrier it names.</summary>
/// <param name="CarrierId">The Carrier's ID when <paramref name="AssignedTo"/> is Carrier; left out for Own fleet.</param>
public sealed record AssignTransportOrderRequest(AssignedTo AssignedTo, string? CarrierId = null);
