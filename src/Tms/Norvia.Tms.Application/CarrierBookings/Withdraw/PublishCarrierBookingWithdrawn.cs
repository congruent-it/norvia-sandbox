using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;

namespace Norvia.Tms.Application.CarrierBookings.Withdraw;

public sealed class PublishCarrierBookingWithdrawn(IOutbox outbox) : INotificationHandler<CarrierBookingWithdrawnEvent>
{
    public ValueTask Handle(CarrierBookingWithdrawnEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        outbox.Add(CarrierBookingWithdrawnV1.From(notification));
        return ValueTask.CompletedTask;
    }
}
