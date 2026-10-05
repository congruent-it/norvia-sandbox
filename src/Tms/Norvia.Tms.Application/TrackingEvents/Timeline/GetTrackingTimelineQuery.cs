using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

/// <summary>
/// A Shipment's timeline: its Tracking events in event-time order, with when each arrived and whether it moved the status (P5),
/// and each of its Proofs of delivery, current and superseded (P6).
/// </summary>
public sealed record GetTrackingTimelineQuery(string ShipmentNumber) : IQuery<Result<ShipmentTimeline>>;
