using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Shipments;

/// <summary>
/// One entry in a Shipment's history of assignments: who the Planner assigned it to, on which Message trail, and when.
/// The first is the planning's; each later one a reassignment's.
/// </summary>
/// <param name="CorrelationId">The Message trail the TMS started for the assignment, as it starts in the TMS UI.</param>
/// <param name="Key">The reassignment's idempotency key; null for the planning's, whose key its Transport Order holds.</param>
public sealed record ShipmentAssignment(CarriedBy CarriedBy, CorrelationId CorrelationId, DateTimeOffset AssignedAt, IdempotencyKey? Key = null);
