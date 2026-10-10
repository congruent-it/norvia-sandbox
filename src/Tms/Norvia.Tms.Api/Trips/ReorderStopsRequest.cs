using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Api.Trips;

/// <param name="Stops">Every Stop of the Trip once, in the order the Driver visits them.</param>
public sealed record ReorderStopsRequest(IReadOnlyList<StopRequest>? Stops);

/// <param name="ShipmentNumber">The Shipment the Stop belongs to, such as "SH-0000012".</param>
/// <param name="Kind">Collection or Delivery.</param>
public sealed record StopRequest(string? ShipmentNumber, StopKind Kind);
