using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing;

public sealed class PublishTransportOrderBillable(IOutbox outbox) : INotificationHandler<TransportOrderBillableEvent>
{
    public ValueTask Handle(TransportOrderBillableEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(TransportOrderBillableV1.From(notification.TransportOrder));
        return ValueTask.CompletedTask;
    }
}
