using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Shipments.Reassign;

/// <summary>
/// The Planner reassigns a Planned Shipment to Own fleet or another Carrier, to fix a mistake before release (P3). The
/// Shipment keeps its Shipment number, records the reassignment in its history of assignments, and ShipmentReassigned is
/// published, all in one transaction. The reassignment starts a Message trail of its own. The reassignment sent again under
/// its key is a repeat, answered as before.
/// </summary>
/// <param name="CarrierId">The Carrier to carry the Shipment; null reassigns it to Own fleet.</param>
/// <param name="ReassignmentKey">The idempotency key that makes a retry of the reassignment a repeat.</param>
/// <param name="CorrelationId">
/// The new Message trail the TMS starts for this reassignment, as it starts in the TMS UI. A repeat answers with the trail
/// of the reassignment it repeats.
/// </param>
public sealed record ReassignShipmentCommand(string ShipmentNumber, string? CarrierId, string ReassignmentKey, string CorrelationId)
    : ICommand<Result<ReassignShipmentResult>>;
