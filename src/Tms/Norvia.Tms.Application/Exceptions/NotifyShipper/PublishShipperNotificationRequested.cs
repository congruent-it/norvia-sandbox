using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

public sealed class PublishShipperNotificationRequested(IOutbox outbox, IReadRepository<TransportOrder> transportOrders)
    : INotificationHandler<ShipperNotificationRequestedEvent>
{
    public async ValueTask Handle(ShipperNotificationRequestedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // The event carries the delivery address's country and postcode, so the Logic App needs no lookup to show times in its time zone.
        var order = await transportOrders.SingleOrDefaultAsync(
            new TransportOrderByNumberSpec(notification.Notification.TransportOrderNumber), cancellationToken);
        outbox.Add(ShipperNotificationRequestedV1.From(notification, order?.Details.Delivery?.Address));
    }
}
