using Ardalis.SharedKernel;

namespace Norvia.Tms.Domain.ShipperNotifications;

/// <summary>The Planner asked to notify the Shipper from an open Exception (P7).</summary>
public sealed class ShipperNotificationRequestedEvent(ShipperNotification notification) : DomainEventBase
{
    public ShipperNotification Notification { get; } = notification;
}
