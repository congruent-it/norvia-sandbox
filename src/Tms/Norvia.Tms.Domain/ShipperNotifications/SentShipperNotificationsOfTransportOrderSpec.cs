using Ardalis.Specification;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The Shipper notifications of a Transport Order that reached the Shipper (Sent), oldest first. Requested and Not delivered ones never did.</summary>
public sealed class SentShipperNotificationsOfTransportOrderSpec : Specification<ShipperNotification>
{
    public SentShipperNotificationsOfTransportOrderSpec(TransportOrderNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        Query.Where(notification => notification.TransportOrderNumber == number && notification.Status == ShipperNotificationStatus.Sent)
            .OrderBy(notification => notification.SentAt)
            .AsNoTracking();
    }
}
