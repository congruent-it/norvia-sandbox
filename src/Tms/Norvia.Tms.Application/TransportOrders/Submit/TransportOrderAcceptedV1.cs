using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

/// <summary>
/// The Canonical Model event <c>TransportOrderAccepted</c>, version 1 (contracts/canonical-model/transport-order-accepted.v1.schema.json).
/// </summary>
public sealed record TransportOrderAcceptedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TransportOrderNumber,
    CanonicalTransportOrder TransportOrder)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "TransportOrderAccepted";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.TransportOrderEvents;

    public static TransportOrderAcceptedV1 From(TransportOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var number = order.Number ?? throw new ArgumentException("Only an Accepted Transport Order has a number.", nameof(order));
        return new(
            Guid.CreateVersion7(),
            order.ReceivedAt,
            order.CorrelationId.Value,
            number.Value,
            CanonicalTransportOrder.From(order));
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.TransportOrderIntake),
        new(TrackingKeys.PartyId, TransportOrder.ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
    ];
}
