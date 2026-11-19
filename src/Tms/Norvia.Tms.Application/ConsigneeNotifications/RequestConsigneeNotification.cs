using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ConsigneeNotifications;

/// <summary>
/// Asks for the Consignee to be told where to track the goods when the Shipment is Collected and when the Planner agrees a delivery window
/// (#983). It adds one event to the outbox of the transaction that did either, and the Consignee notification Logic App takes it from there,
/// so a notification that fails later can't undo the Collected or the agreement, and the Shipper's own notification path is untouched.
/// </summary>
/// <remarks>
/// A Transport Order with no usable email, no tracking code or no record at all asks for nothing, and that is no error: the email is
/// optional. Nothing here logs the address.
/// </remarks>
public sealed class RequestConsigneeNotification(IOutbox outbox, IReadRepository<TransportOrder> transportOrders)
    : INotificationHandler<ShipmentCollectedEvent>, INotificationHandler<ShipmentDeliveryWindowAgreedEvent>
{
    public async ValueTask Handle(ShipmentCollectedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (await CanBeToldAsync(notification.Shipment, cancellationToken))
        {
            outbox.Add(ConsigneeNotificationRequestedV1.From(notification));
        }
    }

    public async ValueTask Handle(ShipmentDeliveryWindowAgreedEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (await CanBeToldAsync(notification.Shipment, cancellationToken))
        {
            outbox.Add(ConsigneeNotificationRequestedV1.From(notification));
        }
    }

    private async Task<bool> CanBeToldAsync(Shipment shipment, CancellationToken cancellationToken) =>
        await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(shipment.TransportOrderNumber), cancellationToken) is { TrackingCode: not null } order
        && ConsigneeEmail.IsValid(order.Details.Delivery?.Consignee?.Email);
}
