using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Application.Exceptions.NotifyShipper;

public sealed class NotifyShipperHandler(
    IReadRepository<ExceptionCase> exceptions,
    IReadRepository<Shipment> shipments,
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<TrackingEvent> trackingEvents,
    IRepository<ShipperNotification> notifications,
    TimeProvider time)
    : ICommandHandler<NotifyShipperCommand, Result<NotifyShipperResult>>
{
    public async ValueTask<Result<NotifyShipperResult>> Handle(NotifyShipperCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExceptionNumber.TryParse(command.ExceptionNumber, out var number)
            || await exceptions.SingleOrDefaultAsync(new ExceptionToResolveSpec(number), cancellationToken) is not { } exception)
        {
            return ExceptionRefused.Because<NotifyShipperResult>(ExceptionRefusal.NotFound(command.ExceptionNumber));
        }

        if (exception.Status != ExceptionStatus.Open)
        {
            return ExceptionRefused.Because<NotifyShipperResult>(
                new(ExceptionRefusalCode.ExceptionNotOpen, Invariant($"Exception {exception.Number} is {exception.Status}, not Open.")));
        }

        // An Exception always has its Shipment and Transport Order, so a missing one is corrupt data, not something the Planner can fix.
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(exception.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Exception {exception.Number} has no Shipment {exception.ShipmentNumber}.");
        var order = await transportOrders.SingleOrDefaultAsync(new TransportOrderByNumberSpec(exception.TransportOrderNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Exception {exception.Number} has no Transport Order {exception.TransportOrderNumber}.");

        var agreed = shipment.AgreedDeliveryWindows.Count > 0 ? shipment.AgreedDeliveryWindows[^1].Window : null;
        var events = await trackingEvents.ListAsync(new TrackingEventsForShipmentsSpec([shipment.Id]), cancellationToken);
        var notification = ShipperNotification.Request(
            exception,
            order.ShipperReference,
            command.Message,
            command.Planner,
            ShipmentEta.Of(events, shipment.DeliveryWindowOf(order.Details)),
            agreed,
            new CorrelationId(command.CorrelationId),
            time.GetUtcNow());

        // The notification and ShipperNotificationRequested are saved together, in one transaction.
        await notifications.AddAsync(notification, cancellationToken);
        await notifications.SaveChangesAsync(cancellationToken);
        return new NotifyShipperResult(
            notification.Id.ToString(), exception.Number.Value, exception.ShipmentNumber.Value, notification.Status, command.CorrelationId);
    }
}
