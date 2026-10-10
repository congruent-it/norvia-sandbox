using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

/// <summary>
/// The Canonical Model event <c>TripNotDelivered</c>, version 1 (contracts/canonical-model/trip-not-delivered.v1.schema.json). The Trip
/// dispatch Logic App gave up sending the Trip's latest version to the Driver app (P11). The Planner can send the version again.
/// Published under the correlation ID of the Dispatch (or send-again) the report continues.
/// </summary>
/// <param name="CorrelationId">The Message trail of the Dispatch or send-again that the report continues.</param>
/// <param name="Cause">The technical cause in plain language.</param>
public sealed record TripNotDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TripNumber,
    int Version,
    string DriverId,
    string Cause)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TripNotDelivered";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TripEvents;

    public static TripNotDeliveredV1 From(TripNotDeliveredEvent notDelivered)
    {
        ArgumentNullException.ThrowIfNull(notDelivered);
        return new(
            Guid.CreateVersion7(),
            notDelivered.ReportedAt,
            notDelivered.CorrelationId.Value,
            notDelivered.Trip.Number.Value,
            notDelivered.Version,
            notDelivered.DriverId.Value,
            notDelivered.Cause);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TripDispatch),
        new(TrackingKeys.TripNumber, TripNumber),
    ];
}
