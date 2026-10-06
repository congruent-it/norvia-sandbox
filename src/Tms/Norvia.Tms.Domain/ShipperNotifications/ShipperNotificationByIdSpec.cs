using Ardalis.Specification;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The Shipper notification with the ID, tracked, so reporting it saves its changes.</summary>
public sealed class ShipperNotificationByIdSpec : SingleResultSpecification<ShipperNotification>
{
    public ShipperNotificationByIdSpec(ShipperNotificationId id) => Query.Where(notification => notification.Id == id);
}
