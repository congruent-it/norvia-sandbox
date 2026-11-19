using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ConsigneeNotifications;

/// <summary>
/// The Canonical Model event <c>ConsigneeNotificationRequested</c>, version 1 (contracts/canonical-model/consignee-notification-requested.v1.schema.json).
/// The Consignee is to be told where to track the goods: the Shipment was Collected (P5), or the Planner agreed a delivery window (P7).
/// </summary>
/// <remarks>
/// It names the Transport Order and nothing about the Consignee: the email address is personal data and the tracking code is a credential
/// (ADR 0052), so neither is in an event, and the email carries the link and nothing else: the tracking page shows the agreed window. The Consignee notification Logic App reads both from the TMS when it sends the email, and an
/// order that has lost its email by then is sent nothing.
/// </remarks>
/// <param name="OccurredAt">When the TMS recorded what the Consignee is told of.</param>
/// <param name="Reason">A <see cref="ConsigneeNotificationReason"/>.</param>
public sealed record ConsigneeNotificationRequestedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string TransportOrderNumber,
    string ShipmentNumber,
    string Reason)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ConsigneeNotificationRequested";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ConsigneeNotificationRequestedV1 From(ShipmentCollectedEvent collected)
    {
        ArgumentNullException.ThrowIfNull(collected);
        return new(
            Guid.CreateVersion7(),
            collected.TrackingEvent.ReceivedAt,
            collected.TrackingEvent.CorrelationId.Value,
            collected.Shipment.TransportOrderNumber.Value,
            collected.Shipment.Number.Value,
            ConsigneeNotificationReason.Collected);
    }

    public static ConsigneeNotificationRequestedV1 From(ShipmentDeliveryWindowAgreedEvent agreed)
    {
        ArgumentNullException.ThrowIfNull(agreed);
        return new(
            Guid.CreateVersion7(),
            agreed.Agreed.AgreedAt,
            agreed.Agreed.CorrelationId.Value,
            agreed.Shipment.TransportOrderNumber.Value,
            agreed.Shipment.Number.Value,
            ConsigneeNotificationReason.DeliveryWindowAgreed);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ConsigneeNotification),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];
}
