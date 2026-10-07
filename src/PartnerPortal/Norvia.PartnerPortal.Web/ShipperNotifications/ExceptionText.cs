using Norvia.PartnerPortal.Application.ShipperNotifications;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.ShipperNotifications;

/// <summary>The words the inbox uses for what a notification is about.</summary>
public static class ExceptionText
{
    public static string Of(ShipperExceptionCode code) => code switch
    {
        ShipperExceptionCode.CollectionFailed => "Collection failed",
        ShipperExceptionCode.DeliveryFailed => "Delivery failed",
        ShipperExceptionCode.Damaged => "Goods damaged",
        ShipperExceptionCode.Reservations => "Delivered with reservations",
        ShipperExceptionCode.Late => "Delivery late",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "The inbox has no words for this code."),
    };

    /// <summary>When the goods are now due, as the notification says: the agreed delivery window if there is one, else the ETA.</summary>
    public static string Arrival(ShipperNotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification.Timing switch
        {
            { DeliverFrom: { } from, DeliverUntil: { } until } => $"Agreed window {LocalDateTimeText.Of(from)} to {LocalDateTimeText.Of(until)}",
            { Eta: { } eta } => $"ETA {LocalDateTimeText.Of(eta)}",
            _ => string.Empty,
        };
    }
}
