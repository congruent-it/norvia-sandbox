using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>
/// The Planner took a Shipment off its Trip before it was collected (P11): its two Stops left the Trip and the Shipment is Planned again,
/// under the correlation ID of the Planner's action, which a Dispatched Trip's new version shares.
/// </summary>
/// <param name="Version">The Trip's new version when the Trip was Dispatched; null for a Draft, which has no version to send.</param>
public sealed class ShipmentTakenOffTripEvent(Trip trip, Shipment shipment, int? version, string reason, CorrelationId correlationId, DateTimeOffset takenOffAt) : DomainEventBase
{
    public Trip Trip { get; } = trip;

    public Shipment Shipment { get; } = shipment;

    public int? Version { get; } = version;

    /// <summary>Why the Planner took the Shipment off, in their words.</summary>
    public string Reason { get; } = reason;

    public CorrelationId CorrelationId { get; } = correlationId;

    public DateTimeOffset TakenOffAt { get; } = takenOffAt;
}
