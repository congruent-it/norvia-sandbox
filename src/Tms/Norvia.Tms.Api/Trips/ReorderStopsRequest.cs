using Norvia.Tms.Application.Trips.ReorderStops;

namespace Norvia.Tms.Api.Trips;

/// <param name="Stops">Every Stop of the Trip once, as a Shipment number and Collection or Delivery, in the order the Driver visits them.</param>
public sealed record ReorderStopsRequest(IReadOnlyList<StopReference>? Stops);
