using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperNotifications;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Agents.Reply;

/// <summary>
/// The Shipper's view of one Transport Order for the reply agent: the same projection the Shipper's own tracking call returns, so the
/// view cannot show more than the Shipper may see, plus the Shipper notifications that were Sent.
/// </summary>
public sealed class GetShipperViewHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IReadRepository<ShipperNotification> notifications)
    : IQueryHandler<GetShipperViewQuery, Result<ShipperView>>
{
    public async ValueTask<Result<ShipperView>> Handle(GetShipperViewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken) is not { } order
            || order.Number is null)
        {
            return Result.NotFound();
        }

        var tracking = await ShipperTransportOrderDetailProjection.ProjectAsync(order, shipments, trackingEvents, proofsOfDelivery, cancellationToken);
        var sent = await notifications.ListAsync(new SentShipperNotificationsOfTransportOrderSpec(number), cancellationToken);
        return new ShipperView(
            tracking,
            [.. sent.Where(notification => notification.SentAt is not null).Select(notification => new ShipperNotificationSent(notification.SentAt!.Value.ToUniversalTime(), notification.Message))]);
    }
}
