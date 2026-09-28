using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Shipments.Reassign;

public sealed class PublishShipmentReassigned(IOutbox outbox) : INotificationHandler<ShipmentReassignedEvent>
{
    public ValueTask Handle(ShipmentReassignedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentReassignedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
