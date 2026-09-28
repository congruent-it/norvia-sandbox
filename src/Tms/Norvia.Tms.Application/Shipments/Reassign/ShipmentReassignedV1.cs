using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.Reassign;

/// <summary>
/// The Canonical Model event <c>ShipmentReassigned</c>, version 1 (contracts/canonical-model/shipment-reassigned.v1.schema.json).
/// It carries what <c>ShipmentPlanned</c> carries, for the new assignment, plus who carried the Shipment before, under the
/// reassignment's own correlation ID.
/// </summary>
/// <param name="AssignedTo"><c>OwnFleet</c> or <c>Carrier</c>, for the new assignment.</param>
/// <param name="CarrierId">The Carrier that carries the Shipment now; null for Own fleet.</param>
/// <param name="PreviousAssignedTo"><c>OwnFleet</c> or <c>Carrier</c>, for the assignment the reassignment replaced.</param>
/// <param name="PreviousCarrierId">The Carrier that carried the Shipment before; null for Own fleet.</param>
public sealed record ShipmentReassignedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    string AssignedTo,
    string? CarrierId,
    string PreviousAssignedTo,
    string? PreviousCarrierId)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipmentReassigned";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipmentReassignedV1 From(ShipmentReassignedEvent reassigned)
    {
        ArgumentNullException.ThrowIfNull(reassigned);
        var shipment = reassigned.Shipment;
        return new(
            Guid.CreateVersion7(),
            reassigned.Reassignment.AssignedAt,
            reassigned.Reassignment.CorrelationId.Value,
            shipment.Number.Value,
            shipment.TransportOrderNumber.Value,
            shipment.ShipperId.Value,
            reassigned.Reassignment.CarriedBy.AssignedTo().ToString(),
            reassigned.Reassignment.CarriedBy.CarrierId?.Value,
            reassigned.Previous.CarriedBy.AssignedTo().ToString(),
            reassigned.Previous.CarriedBy.CarrierId?.Value);
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
        new(ShipmentPlannedV1.AssignedToProperty, AssignedTo),
    ];
}
