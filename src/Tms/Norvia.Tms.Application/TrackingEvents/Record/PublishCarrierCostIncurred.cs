using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.TrackingEvents.Record;

public sealed class PublishCarrierCostIncurred(IOutbox outbox) : INotificationHandler<CarrierCostIncurredEvent>
{
    public ValueTask Handle(CarrierCostIncurredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierCostIncurredV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
