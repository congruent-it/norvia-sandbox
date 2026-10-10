using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.Trips;

/// <summary>The Trip dispatch Logic App reported the Trip's latest version as not delivered (P11), under the correlation ID of the report's own call.</summary>
public sealed class TripNotDeliveredEvent(Trip trip, int version, DriverId driverId, string cause, CorrelationId correlationId, DateTimeOffset reportedAt) : DomainEventBase
{
    public Trip Trip { get; } = trip;

    public int Version { get; } = version;

    public DriverId DriverId { get; } = driverId;

    /// <summary>The technical cause in plain language.</summary>
    public string Cause { get; } = cause;

    public CorrelationId CorrelationId { get; } = correlationId;

    public DateTimeOffset ReportedAt { get; } = reportedAt;
}
