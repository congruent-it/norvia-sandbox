using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Application.ShipperNotifications;

/// <summary>A notification in the Shipper's inbox.</summary>
public sealed record ShipperNotificationView(
    Guid NotificationId,
    DateTimeOffset ReceivedAt,
    string TransportOrderNumber,
    string ShipperReference,
    ShipperExceptionCode ExceptionCode,
    string Message,
    string? Eta,
    string? DeliverFrom,
    string? DeliverUntil,
    string Contact,
    string CorrelationId)
{
    public static ShipperNotificationView From(ShipperNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var content = notification.Content;
        return new(
            notification.NotificationId, notification.ReceivedAt, content.TransportOrderNumber, content.BookingRef, content.ExceptionCode,
            content.Message, content.Eta, content.DeliverFrom, content.DeliverUntil, content.Contact, notification.CorrelationId);
    }
}
