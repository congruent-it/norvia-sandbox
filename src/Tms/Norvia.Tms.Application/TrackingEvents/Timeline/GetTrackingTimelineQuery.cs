using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

/// <summary>A Shipment's Tracking events in event-time order, with when each arrived and whether it moved the status (P5).</summary>
public sealed record GetTrackingTimelineQuery(string ShipmentNumber) : IQuery<Result<IReadOnlyList<TrackingTimelineEntry>>>;
