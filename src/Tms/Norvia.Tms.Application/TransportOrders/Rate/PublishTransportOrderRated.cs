using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

public sealed class PublishTransportOrderRated(IOutbox outbox) : INotificationHandler<TransportOrderRatedEvent>
{
    public ValueTask Handle(TransportOrderRatedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TransportOrderRatedV1.From(notification.TransportOrder, notification.CorrelationId));
        return ValueTask.CompletedTask;
    }
}
