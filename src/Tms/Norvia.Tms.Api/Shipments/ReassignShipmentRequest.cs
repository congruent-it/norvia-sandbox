using Norvia.Tms.Application.Shipments;

namespace Norvia.Tms.Api.Shipments;

/// <summary>Who the Planner reassigns a Planned Shipment to: Own fleet, or the Carrier it names.</summary>
/// <param name="CarrierId">The Carrier's ID when <paramref name="AssignedTo"/> is Carrier; left out for Own fleet.</param>
public sealed record ReassignShipmentRequest(AssignedTo AssignedTo, string? CarrierId = null);
