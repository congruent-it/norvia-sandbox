using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Trips;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The TMS API, the door the Planner plans, releases and handles Exceptions through (ADR 0001). The TMS unreachable is an answer, not an error:
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

    /// <summary>Releases a Planned Shipment. For a Carrier Shipment the answer carries its Carrier booking number.</summary>
    Task<PlannerAnswer> ReleaseAsync(string shipmentNumber, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Creates a Draft Trip with its Vehicle, Driver, planned start and duration and the Shipments on it. <paramref name="idempotencyKey"/> is the same for a retry. The answer carries the Trip's number.</summary>
    Task<PlannerAnswer> CreateTripAsync(TripDraft draft, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Dispatches a Draft Trip to its Driver. The answer carries the correlation ID of the Dispatch, which finds its Message trail.</summary>
    Task<PlannerAnswer> DispatchTripAsync(string tripNumber, CancellationToken cancellationToken);

    /// <summary>The Open Exceptions, or null when the TMS couldn't be asked.</summary>
    Task<IReadOnlyList<OpenExceptionSummary>?> GetOpenExceptionsAsync(CancellationToken cancellationToken);

    /// <summary>Cancels the Confirmed Carrier booking of the Shipment of an Open Exception, before collection.</summary>
    Task<PlannerAnswer> CancelCarrierBookingAsync(string exceptionNumber, string reason, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Resolves an Open Exception as Replanned, with the remark the TMS requires.</summary>
    Task<PlannerAnswer> ResolveReplannedAsync(string exceptionNumber, string remark, CancellationToken cancellationToken);
}
