namespace Norvia.PartnerPortal.Domain.ShipperNotifications;

public readonly record struct ShipperNotificationId(Guid Value)
{
    public static ShipperNotificationId New() => new(Guid.CreateVersion7());
}
