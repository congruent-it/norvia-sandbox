using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <summary>
/// The Canonical Model event <c>TransportOrderRejected</c>, version 1 (contracts/canonical-model/transport-order-rejected.v1.schema.json).
/// </summary>
/// <param name="TransportOrderId">The Rejected booking's ID; it has no Transport Order number.</param>
public sealed record TransportOrderRejectedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    Guid TransportOrderId,
    string ShipperId,
    string? ShipperReference,
    IReadOnlyList<RejectionReasonView> Reasons)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TransportOrderRejected";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TransportOrderEvents;

    public static TransportOrderRejectedV1 From(TransportOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(
            Guid.CreateVersion7(),
            order.ReceivedAt,
            order.CorrelationId.Value,
            order.Id.Value,
            order.ShipperId.Value,
            order.Details.ShipperReference,
            RejectionReasonView.From(order.RejectionReasons));
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new("flow", Flows.TransportOrderIntake),
        new("partyId", ShipperId),
    ];
}
