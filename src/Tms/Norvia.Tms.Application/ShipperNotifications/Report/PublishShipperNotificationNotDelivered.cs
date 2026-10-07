using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

public sealed class PublishShipperNotificationNotDelivered(IOutbox outbox) : INotificationHandler<ShipperNotificationNotDeliveredEvent>
{
    public ValueTask Handle(ShipperNotificationNotDeliveredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipperNotificationNotDeliveredV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
