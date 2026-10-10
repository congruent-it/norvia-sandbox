using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Planner dispatched the Trip (P11): the whole Trip goes to the Driver, under the Dispatch's own correlation ID.</summary>
public sealed class TripDispatchedEvent(
    Trip trip,
    CorrelationId correlationId,
    DateTimeOffset dispatchedAt,
    Vehicle vehicle,
    Driver driver,
    IReadOnlyList<DispatchedShipment> shipments) : DomainEventBase
{
    public Trip Trip { get; } = trip;

    public CorrelationId CorrelationId { get; } = correlationId;

    public DateTimeOffset DispatchedAt { get; } = dispatchedAt;

    public Vehicle Vehicle { get; } = vehicle;

    public Driver Driver { get; } = driver;

    /// <summary>The Shipments on the Trip, in the order they were added.</summary>
    public IReadOnlyList<DispatchedShipment> Shipments { get; } = shipments;
}
