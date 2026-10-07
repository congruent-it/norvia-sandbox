using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The Shipper notification Logic App reported a notification never reached the Shipper (P7).</summary>
public sealed class ShipperNotificationNotDeliveredEvent(ShipperNotification notification) : DomainEventBase
{
    public ShipperNotification Notification { get; } = notification;
}
