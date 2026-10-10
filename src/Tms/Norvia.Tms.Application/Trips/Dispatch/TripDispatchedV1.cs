using System.Xml;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.Dispatch;

/// <summary>
/// The Canonical Model event <c>TripDispatched</c>, version 1 (contracts/canonical-model/trip-dispatched.v1.schema.json). It carries the
/// whole Trip, with its Stops in order, so the Trip dispatch Logic App can send it to the Driver app from the event alone (ADR 0015),
/// under the Dispatch's own correlation ID.
/// </summary>
/// <param name="PlannedDrivingDuration">An ISO 8601 duration such as <c>PT6H</c>.</param>
public sealed record TripDispatchedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TripNumber,
    int Version,
    DateOnly Day,
    TimeOnly PlannedStart,
    string PlannedDrivingDuration,
    TripVehicle Vehicle,
    TripDriver Driver,
    IReadOnlyList<TripDispatchedStop> Stops)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TripDispatched";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TripEvents;

    public static TripDispatchedV1 From(TripDispatchedEvent dispatched)
    {
        ArgumentNullException.ThrowIfNull(dispatched);
        var trip = dispatched.Trip;
        var byId = dispatched.Shipments.ToDictionary(item => item.Shipment.Id);
        return new(
            Guid.CreateVersion7(),
            dispatched.DispatchedAt,
            dispatched.CorrelationId.Value,
            trip.Number.Value,
            trip.Version,
            trip.Day,
            trip.PlannedStart ?? throw new InvalidOperationException($"Trip {trip.Number} was dispatched without a planned start."),
            XmlConvert.ToString(trip.PlannedDrivingDuration ?? throw new InvalidOperationException($"Trip {trip.Number} was dispatched without a planned driving duration.")),
            new TripVehicle(dispatched.Vehicle.Id.Value, dispatched.Vehicle.Registration, dispatched.Vehicle.Type.ToString()),
            new TripDriver(dispatched.Driver.Id.Value, dispatched.Driver.Name),
            [.. trip.Stops.Select(stop => TripDispatchedStop.From(stop, byId[stop.ShipmentId]))]);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TripDispatch),
        new(TrackingKeys.TripNumber, TripNumber),
    ];
}
