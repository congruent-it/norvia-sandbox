using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class PublishShipmentInTransit(IOutbox outbox) : INotificationHandler<ShipmentInTransitEvent>
{
    public ValueTask Handle(ShipmentInTransitEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentInTransitV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
