using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
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
/// optional. Nothing here logs the address. Checking the Transport Order can never fail the Collected or the agreement it rides in: if the
/// read throws, no notification is asked for and the failure is logged without any data.
/// </remarks>
public sealed partial class RequestConsigneeNotification(IOutbox outbox, IReadRepository<TransportOrder> transportOrders, ILogger<RequestConsigneeNotification> logger)
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

    private async Task<bool> CanBeToldAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        try
        {
            return await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(shipment.TransportOrderNumber), cancellationToken) is { TrackingCode: not null } order
                && ConsigneeEmail.IsValid(order.Details.Delivery?.Consignee?.Email);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The Consignee's notification is the lesser job: the Shipper's path and the Collected itself must not fail for it.
            LogCheckFailed(exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Transport Order couldn't be checked for a Consignee notification ({ExceptionType}); none is asked for")]
    private partial void LogCheckFailed(string exceptionType);
}
