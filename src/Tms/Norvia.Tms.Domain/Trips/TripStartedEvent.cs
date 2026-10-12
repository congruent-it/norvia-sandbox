using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Driver started the Trip (P12), under the correlation ID of the Driver's action.</summary>
public sealed class TripStartedEvent(Trip trip, DriverId driverId, CorrelationId correlationId, DateTimeOffset startedAt) : DomainEventBase
{
    public Trip Trip { get; } = trip;

    public DriverId DriverId { get; } = driverId;

    public CorrelationId CorrelationId { get; } = correlationId;

    public DateTimeOffset StartedAt { get; } = startedAt;
}
