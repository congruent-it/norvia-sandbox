using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.Timeline;

/// <summary>A Shipper notification on a Shipment's timeline (P7), with where it stands.</summary>
/// <param name="NotDeliveredMessage">The technical cause when it is Not delivered; null otherwise.</param>
public sealed record ShipperNotificationView(
    string Message,
    string Planner,
    ShipperNotificationStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SentAt,
    string? NotDeliveredMessage)
{
    public static ShipperNotificationView Of(ShipperNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new(notification.Message, notification.Planner, notification.Status, notification.RequestedAt, notification.SentAt, notification.NotDeliveredMessage);
    }
}
