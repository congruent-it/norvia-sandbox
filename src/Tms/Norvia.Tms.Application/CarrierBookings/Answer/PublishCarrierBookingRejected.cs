using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

public sealed class PublishCarrierBookingRejected(IOutbox outbox) : INotificationHandler<CarrierBookingRejectedEvent>
{
    public ValueTask Handle(CarrierBookingRejectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierBookingRejectedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
