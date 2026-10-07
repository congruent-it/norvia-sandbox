using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

/// <summary>
/// The Canonical Model event <c>ShipperNotificationRequested</c>, version 1 (contracts/canonical-model/shipper-notification-requested.v1.schema.json).
/// The Planner notified the Shipper from an open Exception (P7), under the action's own correlation ID. The Shipper notification
/// Logic App sends the message from this event.
/// </summary>
/// <param name="NotificationId">The key the Partner portal treats a notification by, so a repeated call is ignored.</param>
/// <param name="ExceptionType">What went wrong: <c>CollectionFailed</c>, <c>DeliveryFailed</c>, <c>Damaged</c>, <c>Reservations</c> or <c>LateDelivery</c>.</param>
/// <param name="Message">What the Planner wrote, starting from the template for the type.</param>
/// <param name="Eta">The Shipment's ETA, when it has one.</param>
/// <param name="AgreedDeliveryWindowEarliest">The earliest time of the delivery window the Planner agreed, when there is one.</param>
/// <param name="AgreedDeliveryWindowLatest">The latest time of the delivery window the Planner agreed, when there is one.</param>
public sealed record ShipperNotificationRequestedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string NotificationId,
    string ExceptionNumber,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    string? ShipperReference,
    string ExceptionType,
    string Message,
    DateTimeOffset? Eta,
    DateTimeOffset? AgreedDeliveryWindowEarliest,
    DateTimeOffset? AgreedDeliveryWindowLatest,
    string Planner)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipperNotificationRequested";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipperNotificationRequestedV1 From(ShipperNotificationRequestedEvent requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var notification = requested.Notification;
        return new(
            Guid.CreateVersion7(),
            notification.RequestedAt,
            notification.CorrelationId.Value,
            notification.Id.ToString(),
            notification.ExceptionNumber.Value,
            notification.ShipmentNumber.Value,
            notification.TransportOrderNumber.Value,
            notification.ShipperId.Value,
            notification.ShipperReference,
            notification.Type.ToString(),
            notification.Message,
            notification.Eta,
            notification.AgreedDeliveryEarliest,
            notification.AgreedDeliveryLatest,
            notification.Planner);
    }

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ExceptionHandling),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];
}
