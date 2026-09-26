using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

public sealed class PublishTransportOrderRatingFailed(IOutbox outbox) : INotificationHandler<TransportOrderRatingFailedEvent>
{
    public ValueTask Handle(TransportOrderRatingFailedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TransportOrderRatingFailedV1.From(notification.TransportOrder));
        return ValueTask.CompletedTask;
    }
}
