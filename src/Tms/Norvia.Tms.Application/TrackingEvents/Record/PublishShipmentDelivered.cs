using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class PublishShipmentDelivered(IOutbox outbox) : INotificationHandler<ShipmentDeliveredEvent>
{
    public ValueTask Handle(ShipmentDeliveredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentDeliveredV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
