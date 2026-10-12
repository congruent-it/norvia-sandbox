using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Driver ended the Trip (P12), under the correlation ID of the Driver's action.</summary>
/// <param name="shipments">The Shipments delivered, not delivered and not collected.</param>
public sealed class TripCompletedEvent(
    Trip trip,
    DriverId driverId,
    CorrelationId correlationId,
    DateTimeOffset endedAt,
    TripShipmentGroups shipments) : DomainEventBase
{
    public Trip Trip { get; } = trip;

    public TripNumber TripNumber { get; } = trip.Number;

    /// <summary>The Trip's version when it ended. Ending doesn't change it.</summary>
    public int Version { get; } = trip.Version;

    public DriverId DriverId { get; } = driverId;

    public CorrelationId CorrelationId { get; } = correlationId;

    public DateTimeOffset EndedAt { get; } = endedAt;

    public TripShipmentGroups Shipments { get; } = shipments;

    public IReadOnlyList<ShipmentNumber> Delivered => Shipments.Delivered;

    public IReadOnlyList<ShipmentNumber> NotDelivered => Shipments.NotDelivered;

    public IReadOnlyList<ShipmentNumber> NotCollected => Shipments.NotCollected;
}
