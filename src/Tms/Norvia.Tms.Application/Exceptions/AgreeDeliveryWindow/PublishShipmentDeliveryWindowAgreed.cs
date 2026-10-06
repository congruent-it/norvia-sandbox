using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

public sealed class PublishShipmentDeliveryWindowAgreed(IOutbox outbox) : INotificationHandler<ShipmentDeliveryWindowAgreedEvent>
{
    public ValueTask Handle(ShipmentDeliveryWindowAgreedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(ShipmentDeliveryWindowAgreedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
