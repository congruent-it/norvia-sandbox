using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class PublishShipmentCollected(IOutbox outbox) : INotificationHandler<ShipmentCollectedEvent>
{
    public ValueTask Handle(ShipmentCollectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentCollectedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
