using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Report;

public sealed class PublishCarrierBookingNotDelivered(IOutbox outbox) : INotificationHandler<CarrierBookingNotDeliveredEvent>
{
    public ValueTask Handle(CarrierBookingNotDeliveredEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierBookingNotDeliveredV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
