using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Submit;

public sealed class PublishTransportOrderAccepted(IOutbox outbox) : INotificationHandler<TransportOrderAcceptedEvent>
{
    public ValueTask Handle(TransportOrderAcceptedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TransportOrderAcceptedV1.From(notification.TransportOrder));
        return ValueTask.CompletedTask;
    }
}
