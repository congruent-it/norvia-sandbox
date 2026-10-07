using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.ListInProgress;

/// <summary>A Shipment on its way, in the list of Shipments in progress (P5).</summary>
/// <param name="CarriedBy">Who carries it, so the Carrier for a Carrier Shipment.</param>
/// <param name="LastTrackingEvent">The last Tracking event to arrive, so the one the ETA follows; null when none has been reported yet.</param>
/// <param name="Eta">
/// The last ETA a Carrier reported, or the Transport Order's delivery window's latest time when none was; null when
/// neither exists.
/// </param>
/// <param name="HasProofOfDelivery">
/// Whether a Proof of delivery has arrived before the Delivered Tracking event, which may have been lost. Only a marker for the Planner;
/// the Shipment's status is still the Tracking events' to move.
/// </param>
/// <param name="HasOpenException">Whether the Shipment has an open Exception (P7), which the Planner works from the list of Open Exceptions.</param>
/// <param name="IsStale">Whether its next expected Tracking event is late. Only a flag for the Planner; Stale Shipments are listed first.</param>
public sealed record ShipmentInProgressSummary(
    string ShipmentNumber,
    CarriedByView CarriedBy,
    ShipmentStatus Status,
    LastTrackingEventView? LastTrackingEvent,
    DateTimeOffset? Eta,
    bool IsStale,
    bool HasProofOfDelivery,
    bool HasOpenException);
