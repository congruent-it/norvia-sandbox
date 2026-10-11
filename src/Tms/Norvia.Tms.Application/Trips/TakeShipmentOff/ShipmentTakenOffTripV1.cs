using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.TakeShipmentOff;

/// <summary>
/// The Canonical Model event <c>ShipmentTakenOffTrip</c>, version 1 (contracts/canonical-model/shipment-taken-off-trip.v1.schema.json). The
/// Planner took a Shipment off its Trip before it was collected (P11), so it is Planned again. Published to the Shipment events topic under the
/// correlation ID of the Planner's action, which the Trip's new version shares.
/// </summary>
/// <param name="Version">The Trip's new version when the Trip was Dispatched; null for a Draft.</param>
/// <param name="Reason">Why the Planner took the Shipment off, in their words.</param>
public sealed record ShipmentTakenOffTripV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string TripNumber,
    int? Version,
    string Reason)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipmentTakenOffTrip";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipmentTakenOffTripV1 From(ShipmentTakenOffTripEvent takenOff)
    {
        ArgumentNullException.ThrowIfNull(takenOff);
        return new(
            Guid.CreateVersion7(),
            takenOff.TakenOffAt,
            takenOff.CorrelationId.Value,
            takenOff.Shipment.Number.Value,
            takenOff.Shipment.TransportOrderNumber.Value,
            takenOff.Trip.Number.Value,
            takenOff.Version,
            takenOff.Reason);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TripDispatch),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
        new(TrackingKeys.TripNumber, TripNumber),
    ];
}
