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

public sealed record TripVehicle(string VehicleId, string Registration, string VehicleType);

public sealed record TripDriver(string DriverId, string Name);

/// <param name="Sequence">The Stop's place in the sequence, counting from 1.</param>
/// <param name="Kind"><c>Collection</c> or <c>Delivery</c>.</param>
/// <param name="Window">The collection window, or for a delivery the last agreed one, otherwise the Shipper's.</param>
/// <param name="Consignee">Who receives the goods at a delivery Stop; null at a collection Stop.</param>
public sealed record TripDispatchedStop(
    int Sequence,
    string Kind,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    CanonicalAddress? Address,
    CanonicalTimeWindow? Window,
    IReadOnlyList<CanonicalGoodsLine> GoodsLines,
    CanonicalConsignee? Consignee)
{
    public static TripDispatchedStop From(TripStop stop, DispatchedShipment onTrip)
    {
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(onTrip);
        var (shipment, details) = (onTrip.Shipment, onTrip.Details);
        var collects = stop.Kind == StopKind.Collection;
        return new(
            stop.Position + 1,
            stop.Kind.ToString(),
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            shipment.ShipperId.Value,
            CanonicalAddress.From(collects ? details.Collection?.Address : details.Delivery?.Address),
            CanonicalTimeWindow.From(collects ? details.Collection?.Window : shipment.DeliveryWindowOf(details)),
            [.. details.GoodsLines.Select(CanonicalGoodsLine.From)],
            collects ? null : CanonicalConsignee.From(details.Delivery?.Consignee));
    }
}
