using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

public sealed class PublishTransportOrderRejected(IOutbox outbox) : INotificationHandler<TransportOrderRejectedEvent>
{
    public ValueTask Handle(TransportOrderRejectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TransportOrderRejectedV1.From(notification.TransportOrder));
        return ValueTask.CompletedTask;
    }
}
