using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.Exceptions.CancelCarrierBooking;

public sealed class PublishCarrierBookingCancelled(IOutbox outbox) : INotificationHandler<CarrierBookingCancelledEvent>
{
    public ValueTask Handle(CarrierBookingCancelledEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierBookingCancelledV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
