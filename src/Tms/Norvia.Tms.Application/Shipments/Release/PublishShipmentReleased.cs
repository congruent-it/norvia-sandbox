using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.Release;

public sealed class PublishShipmentReleased(IOutbox outbox) : INotificationHandler<ShipmentReleasedEvent>
{
    public ValueTask Handle(ShipmentReleasedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentReleasedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
