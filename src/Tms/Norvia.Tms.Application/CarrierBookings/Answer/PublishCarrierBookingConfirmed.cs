using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Answer;

public sealed class PublishCarrierBookingConfirmed(IOutbox outbox) : INotificationHandler<CarrierBookingConfirmedEvent>
{
    public ValueTask Handle(CarrierBookingConfirmedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierBookingConfirmedV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
