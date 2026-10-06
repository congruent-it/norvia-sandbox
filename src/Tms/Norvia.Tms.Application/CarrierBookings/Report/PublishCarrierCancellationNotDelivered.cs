using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

public sealed class PublishCarrierCancellationNotDelivered(IOutbox outbox) : INotificationHandler<CarrierCancellationNotDeliveredEvent>
{
    public ValueTask Handle(CarrierCancellationNotDeliveredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierCancellationNotDeliveredV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
