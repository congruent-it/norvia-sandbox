using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>The Planner's release of a Shipment: on which Message trail, when, and under which key.</summary>
/// <param name="CorrelationId">The Message trail the TMS started for the release, as it starts in the TMS UI.</param>
/// <param name="Key">The release's idempotency key, so the release sent again is known as a repeat.</param>
public sealed record ShipmentRelease(CorrelationId CorrelationId, DateTimeOffset ReleasedAt, IdempotencyKey Key);
