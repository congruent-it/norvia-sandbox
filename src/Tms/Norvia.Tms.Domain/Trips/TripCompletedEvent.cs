using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Driver ended the Trip (P12), under the correlation ID of the Driver's action.</summary>
/// <param name="Delivered">The Shipments whose delivery Stop is Delivered.</param>
/// <param name="NotDelivered">The Shipments collected but not delivered, which are Awaiting re-delivery now.</param>
/// <param name="NotCollected">The Shipments whose collection failed, which are Planned again.</param>
public sealed class TripCompletedEvent(
    Trip trip,
    DriverId driverId,
    CorrelationId correlationId,
    DateTimeOffset endedAt,
    IReadOnlyList<ShipmentNumber> delivered,
    IReadOnlyList<ShipmentNumber> notDelivered,
    IReadOnlyList<ShipmentNumber> notCollected) : DomainEventBase
{
    public Trip Trip { get; } = trip;

    public TripNumber TripNumber { get; } = trip.Number;

    /// <summary>The Trip's version when it ended. Ending doesn't change it.</summary>
    public int Version { get; } = trip.Version;

    public DriverId DriverId { get; } = driverId;

    public CorrelationId CorrelationId { get; } = correlationId;

    public DateTimeOffset EndedAt { get; } = endedAt;

    public IReadOnlyList<ShipmentNumber> Delivered { get; } = delivered;

    public IReadOnlyList<ShipmentNumber> NotDelivered { get; } = notDelivered;

    public IReadOnlyList<ShipmentNumber> NotCollected { get; } = notCollected;
}
