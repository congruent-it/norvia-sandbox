using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.TransportOrders.Assign;

public sealed class PublishShipmentPlanned(IOutbox outbox) : INotificationHandler<ShipmentPlannedEvent>
{
    public ValueTask Handle(ShipmentPlannedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentPlannedV1.From(notification.Shipment));
        return ValueTask.CompletedTask;
    }
}
