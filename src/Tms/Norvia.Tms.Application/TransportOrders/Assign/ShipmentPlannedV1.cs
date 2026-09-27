using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TransportOrders.Assign;

/// <summary>
/// The Canonical Model event <c>ShipmentPlanned</c>, version 1 (contracts/canonical-model/shipment-planned.v1.schema.json).
/// It carries the correlation ID of the Planner's assignment, not the intake's.
/// </summary>
/// <param name="AssignedTo"><c>OwnFleet</c> or <c>Carrier</c>.</param>
/// <param name="CarrierId">The Carrier that carries the Shipment; null for Own fleet.</param>
public sealed record ShipmentPlannedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    string AssignedTo,
    string? CarrierId)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    /// <summary>The message property that says whether the Shipment is carried by Own fleet or a Carrier.</summary>
    public const string AssignedToProperty = "assignedTo";

    public override string EventType => "ShipmentPlanned";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipmentPlannedV1 From(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return new(
            Guid.CreateVersion7(),
            shipment.PlannedAt,
            shipment.CorrelationId.Value,
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            shipment.ShipperId.Value,
            (shipment.CarriedBy.IsOwnFleet ? Shipments.AssignedTo.OwnFleet : Shipments.AssignedTo.Carrier).ToString(),
            shipment.CarriedBy.CarrierId?.Value);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TransportPlanning),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];

    protected override IEnumerable<KeyValuePair<string, object>> FilterProperties() =>
    [
        new(AssignedToProperty, AssignedTo),
    ];
}
