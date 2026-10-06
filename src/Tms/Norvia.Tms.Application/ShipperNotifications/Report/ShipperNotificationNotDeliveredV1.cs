using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

/// <summary>
/// The Canonical Model event <c>ShipperNotificationNotDelivered</c>, version 1
/// (contracts/canonical-model/shipper-notification-not-delivered.v1.schema.json). The Shipper notification Logic App's message never
/// reached the Shipper (P7): the Exception shows the Shipper wasn't told, and the Planner can notify again or phone the Shipper.
/// Published under the correlation ID of the Planner's action the report continues.
/// </summary>
/// <param name="CorrelationId">The Planner's action's own correlation ID, which the report continues.</param>
/// <param name="StatusCode">The Shipper endpoint's HTTP status code, or the technical failure's own code.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ShipperNotificationNotDeliveredV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string NotificationId,
    string ExceptionNumber,
    string ShipmentNumber,
    string TransportOrderNumber,
    string ShipperId,
    int StatusCode,
    string Message)
    : IntegrationEvent(EventId, OccurredAt, CorrelationId)
{
    public override string EventType => "ShipperNotificationNotDelivered";

    public override int EventVersion => 1;

    protected override string TopicName => Topics.ShipmentEvents;

    public static ShipperNotificationNotDeliveredV1 From(ShipperNotificationNotDeliveredEvent notDelivered)
    {
        ArgumentNullException.ThrowIfNull(notDelivered);
        var notification = notDelivered.Notification;
        return new(
            Guid.CreateVersion7(),
            notification.NotDeliveredAt ?? throw NotReported(notification),
            notification.CorrelationId.Value,
            notification.Id.ToString(),
            notification.ExceptionNumber.Value,
            notification.ShipmentNumber.Value,
            notification.TransportOrderNumber.Value,
            notification.ShipperId.Value,
            notification.NotDeliveredStatusCode ?? throw NotReported(notification),
            notification.NotDeliveredMessage ?? throw NotReported(notification));
    }

    private static InvalidOperationException NotReported(ShipperNotification notification) =>
        new($"Shipper notification {notification.Id} raised ShipperNotificationNotDelivered without being reported Not delivered.");

    protected override IEnumerable<KeyValuePair<string, object>> TrackingProperties() =>
    [
        new(TrackingKeys.Flow, Flows.ExceptionHandling),
        new(TrackingKeys.PartyId, ShipperId),
        new(TrackingKeys.TransportOrderNumber, TransportOrderNumber),
        new(TrackingKeys.ShipmentNumber, ShipmentNumber),
    ];
}
