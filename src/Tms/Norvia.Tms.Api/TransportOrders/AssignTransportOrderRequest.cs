using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Api.TransportOrders;

/// <summary>Who the Planner assigns a Transport Order to: Own fleet, or the Carrier it names.</summary>
/// <param name="CarrierId">The Carrier's ID when <paramref name="AssignedTo"/> is Carrier; left out for Own fleet.</param>
/// <param name="AgentName">Optional, with <paramref name="AgentRunId"/>: the Agent whose Suggestion the Planner acted on, which the assignment's trace then names.</param>
/// <param name="AgentRunId">Optional, with <paramref name="AgentName"/>: that Agent's run.</param>
public sealed record AssignTransportOrderRequest(AssignedTo AssignedTo, string? CarrierId = null, string? AgentName = null, string? AgentRunId = null);
