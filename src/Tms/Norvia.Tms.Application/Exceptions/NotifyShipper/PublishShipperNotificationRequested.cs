using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

public sealed class PublishShipperNotificationRequested(IOutbox outbox) : INotificationHandler<ShipperNotificationRequestedEvent>
{
    public ValueTask Handle(ShipperNotificationRequestedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipperNotificationRequestedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
