using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// One Shipment's collection or its delivery, at its place in a Trip's sequence (ADR 0007). Where and when come from the Shipment, so
/// a Stop holds nothing but which Shipment, which of the two, and its place.
/// </summary>
/// <param name="Position">The Stop's place in the sequence, counting from 0.</param>
public sealed record TripStop(ShipmentId ShipmentId, StopKind Kind, int Position);
