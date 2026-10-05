using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.ListInProgress;

/// <summary>A Shipment on its way, in the list of Shipments in progress (P5).</summary>
/// <param name="CarriedBy">Who carries it, so the Carrier for a Carrier Shipment.</param>
/// <param name="LastTrackingEvent">The Tracking event with the latest event time; null when none has been reported yet.</param>
/// <param name="Eta">
/// The last ETA a Carrier reported, or the Transport Order's delivery window's latest time when none was; null when
/// neither exists.
/// </param>
public sealed record ShipmentInProgressSummary(
    string ShipmentNumber,
    CarriedByView CarriedBy,
    ShipmentStatus Status,
    LastTrackingEventView? LastTrackingEvent,
    DateTimeOffset? Eta);
