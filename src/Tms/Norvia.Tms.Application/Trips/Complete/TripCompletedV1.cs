using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Complete;

/// <summary>
/// The Canonical Model event <c>TripCompleted</c>, version 1 (contracts/canonical-model/trip-completed.v1.schema.json). The Driver ended an
/// In progress Trip, so it is Completed (P12). Published under the correlation ID of the Driver's End Trip action.
/// </summary>
/// <param name="CorrelationId">The Message trail of the Driver's action.</param>
/// <param name="Version">The Trip's version when it ended.</param>
/// <param name="EndedAt">When the Trip ended.</param>
/// <param name="DeliveredShipments">The Shipments delivered.</param>
/// <param name="NotDeliveredShipments">The Shipments collected but not delivered, now Awaiting re-delivery.</param>
/// <param name="NotCollectedShipments">The Shipments not collected, now Planned again.</param>
public sealed record TripCompletedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TripNumber,
    int Version,
    string DriverId,
    DateTimeOffset EndedAt,
    IReadOnlyList<string> DeliveredShipments,
    IReadOnlyList<string> NotDeliveredShipments,
    IReadOnlyList<string> NotCollectedShipments)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TripCompleted";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TripEvents;

    public static TripCompletedV1 From(TripCompletedEvent completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        return new(
            Guid.CreateVersion7(),
            completed.EndedAt,
            completed.CorrelationId.Value,
            completed.TripNumber.Value,
            completed.Version,
            completed.DriverId.Value,
            completed.EndedAt,
            [.. completed.Delivered.Select(number => number.Value)],
            [.. completed.NotDelivered.Select(number => number.Value)],
            [.. completed.NotCollected.Select(number => number.Value)]);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TripExecution),
        new(TrackingKeys.TripNumber, TripNumber),
    ];
}
