using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Start;

/// <summary>
/// The Canonical Model event <c>TripStarted</c>, version 1 (contracts/canonical-model/trip-started.v1.schema.json). The Driver started a
/// Dispatched Trip on its day, so it is In progress (P12). Published under the correlation ID of the Driver's Start Trip action.
/// </summary>
/// <param name="CorrelationId">The Message trail of the Driver's action.</param>
/// <param name="Version">The Trip's version when it started.</param>
/// <param name="StartedAt">When the Trip started.</param>
public sealed record TripStartedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TripNumber,
    int Version,
    string DriverId,
    DateTimeOffset StartedAt)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TripStarted";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TripEvents;

    public static TripStartedV1 From(TripStartedEvent started)
    {
        ArgumentNullException.ThrowIfNull(started);
        return new(
            Guid.CreateVersion7(),
            started.StartedAt,
            started.CorrelationId.Value,
            started.Trip.Number.Value,
            started.Trip.Version,
            started.DriverId.Value,
            started.StartedAt);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TripExecution),
        new(TrackingKeys.TripNumber, TripNumber),
    ];
}
