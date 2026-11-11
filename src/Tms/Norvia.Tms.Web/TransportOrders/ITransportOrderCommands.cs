using Norvia.Agents;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Trips.ReorderStops;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Web.Capacity;
using Norvia.Tms.Web.Exceptions;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.Trips;

namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// What the TMS UI changes about Transport Orders and their Shipments. It reads through the query side in process, but
/// changes things only through the TMS API, as any other caller does.
/// </summary>
public interface ITransportOrderCommands
{
    /// <summary>Re-rates a Transport Order whose Rating failed, typically after its Rate Card was corrected.</summary>
    Task<ReRateAnswer> ReRateAsync(string transportOrderNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Assigns one Transport Order to Own fleet or to a Carrier, which plans its one Shipment. A multi-select assigns
    /// each Transport Order with a call of its own, so each succeeds or is refused on its own.
    /// </summary>
    /// <param name="carrierId">The Carrier to carry it; null for Own fleet.</param>
    /// <param name="actingOn">The Agent run whose Suggestion the Planner acted on, which the assignment's trace then names; null when they acted on their own.</param>
    Task<AssignAnswer> AssignAsync(string transportOrderNumber, string? carrierId, AgentRun? actingOn, CancellationToken cancellationToken);

    /// <summary>Reassigns a Planned Shipment to Own fleet or another Carrier, to fix a mistake before release.</summary>
    /// <param name="carrierId">The Carrier to carry it; null for Own fleet.</param>
    Task<ReassignAnswer> ReassignAsync(string shipmentNumber, string? carrierId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases one Planned Shipment, handing it over to its Carrier or to Own fleet's Trips. A multi-select releases
    /// each Shipment with a call of its own, so each succeeds or is refused on its own.
    /// </summary>
    Task<ReleaseAnswer> ReleaseAsync(string shipmentNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Releases one Planned Carrier Shipment at a Spot price, after its release failed on the Carrier Rate Card (P9).
    /// </summary>
    /// <param name="amount">In EUR, without VAT.</param>
    /// <param name="remark">Where the price comes from, such as "agreed with Van Dijk by phone, €480".</param>
    Task<ReleaseAnswer> ReleaseWithSpotPriceAsync(string shipmentNumber, decimal amount, string remark, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws one Requested Carrier booking before the Carrier answers, which puts its Shipment back to Planned. Each
    /// withdrawal is a call of its own, so one is refused on its own.
    /// </summary>
    Task<WithdrawAnswer> WithdrawAsync(string carrierBookingNumber, CancellationToken cancellationToken);

    /// <summary>Notifies the Shipper from an open Exception, with the message the Planner wrote, starting from the template for its type (P7).</summary>
    /// <param name="actingOn">The Agent run whose draft the message was sent from, edited or not, which the notification's trace then names; null when the Planner wrote it themselves.</param>
    Task<ExceptionAnswer> NotifyShipperAsync(string exceptionNumber, string message, AgentRun? actingOn, CancellationToken cancellationToken);

    /// <summary>Cancels the Confirmed Carrier booking of the Shipment of an open Exception, with a reason, so the Shipment goes back to Planned (P7).</summary>
    Task<ExceptionAnswer> CancelCarrierBookingAsync(string exceptionNumber, string reason, CancellationToken cancellationToken);

    /// <summary>Agrees a delivery window for the Shipment of an open Exception, after agreeing a redelivery with the Carrier outside the TMS (P7).</summary>
    Task<ExceptionAnswer> AgreeDeliveryWindowAsync(string exceptionNumber, DateTimeOffset earliest, DateTimeOffset latest, CancellationToken cancellationToken);

    /// <summary>Adds a free-text note to an open Exception (P7).</summary>
    /// <param name="actingOn">The Agent run whose draft the note was saved from, edited or not, which the note's trace then names; null when the Planner wrote it themselves.</param>
    Task<ExceptionAnswer> AddNoteAsync(string exceptionNumber, string text, AgentRun? actingOn, CancellationToken cancellationToken);

    /// <summary>Resolves an open Exception with an outcome and the remark every outcome needs (P7).</summary>
    Task<ExceptionAnswer> ResolveAsync(string exceptionNumber, ExceptionOutcome outcome, string remark, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a Delivered, Not yet billable Transport Order without a Proof of delivery, with the remark every release
    /// needs (P9). The order becomes Billable only when nothing else blocks it.
    /// </summary>
    /// <param name="actingOn">The Agent run whose Suggestion Customer service acted on, which the release's trace then names; null when they acted on their own.</param>
    Task<ReleaseWithoutProofOfDeliveryAnswer> ReleaseWithoutProofOfDeliveryAsync(string transportOrderNumber, string remark, AgentRun? actingOn, CancellationToken cancellationToken);

    /// <summary>The Planner records an Absence for a Driver, for the days from first to last (P10).</summary>
    Task<CapacityAnswer> RecordAbsenceAsync(PeriodEntry entry, CancellationToken cancellationToken);

    /// <summary>The Planner removes an Absence (P10).</summary>
    Task<CapacityAnswer> RemoveAbsenceAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The Planner takes a Vehicle off the road for the days from first to last (P10).</summary>
    Task<CapacityAnswer> RecordOffTheRoadAsync(PeriodEntry entry, CancellationToken cancellationToken);

    /// <summary>The Planner removes an Off the road day (P10).</summary>
    Task<CapacityAnswer> RemoveOffTheRoadAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The Planner changes a Driver's Roster (P10); a weekday left out is a day off.</summary>
    Task<CapacityAnswer> ChangeRosterAsync(string driverId, IReadOnlyList<RosterWeekdayView> weekdays, CancellationToken cancellationToken);

    /// <summary>The Planner sets one Driver's Shift on a single open day (P10).</summary>
    Task<CapacityAnswer> EditShiftAsync(DateOnly day, string driverId, TimeOnly startsAt, TimeOnly endsAt, CancellationToken cancellationToken);

    /// <summary>The Planner creates a Draft Trip for an open day, with or without its Vehicle, Driver, planned start and driving duration (P11).</summary>
    Task<TripAnswer> CreateDraftTripAsync(TripEntry entry, CancellationToken cancellationToken);

    /// <summary>The Planner picks the Vehicle, Driver, planned start and driving duration of a Draft Trip, or gives them back with null (P11).</summary>
    Task<TripAnswer> ChangeDraftTripAsync(string number, TripEntry entry, CancellationToken cancellationToken);

    /// <summary>The Planner puts Shipments on a Draft Trip: each adds its collection and delivery Stops at the end. All go on or none (P11).</summary>
    Task<TripAnswer> AddShipmentsToTripAsync(string number, IReadOnlyList<string> shipmentNumbers, CancellationToken cancellationToken);

    /// <summary>The Planner puts the Stops of a Draft Trip in order: every Stop once. A delivery before its collection is refused (P11).</summary>
    Task<TripAnswer> ReorderTripStopsAsync(string number, IReadOnlyList<StopReference> stops, CancellationToken cancellationToken);

    /// <summary>The Planner dispatches a Draft Trip to its Driver: the TMS re-checks every rule, then sends it as version 1 (P11).</summary>
    Task<TripAnswer> DispatchTripAsync(string number, CancellationToken cancellationToken);

    /// <summary>The Planner sends a Not delivered version of a Dispatched Trip to the Driver app again: the same version, under a new Message trail (P11).</summary>
    Task<TripAnswer> SendTripAgainAsync(string number, int version, CancellationToken cancellationToken);

    /// <summary>The Planner discards a Draft Trip, which gives its Vehicle and Driver back (P11).</summary>
    Task<TripAnswer> DiscardDraftTripAsync(string number, CancellationToken cancellationToken);
}
