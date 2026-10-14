using Norvia.Simulator.Domain.Planner;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The TMS API, the door the Planner plans through (ADR 0001). The TMS unreachable is an answer, not an error:
/// <see cref="Domain.Log.LogOutcome.PlanningNotReceived"/>.
/// </summary>
public interface ITmsApi
{
    /// <summary>The Own fleet's open days with their capacity and what is planned on them, or null when the TMS couldn't be asked.</summary>
    Task<IReadOnlyList<FleetDay>?> GetOwnFleetDaysAsync(CancellationToken cancellationToken);

    /// <summary>Assigns an Accepted Transport Order, which plans its Shipment. <paramref name="idempotencyKey"/> is the same for a retry.</summary>
    Task<PlannerAnswer> AssignAsync(string transportOrderNumber, Assignee assignee, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Reassigns a Planned Shipment to someone else.</summary>
    Task<PlannerAnswer> ReassignAsync(string shipmentNumber, Assignee assignee, string idempotencyKey, CancellationToken cancellationToken);
}
