using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

public sealed class GetShipperTransportOrderHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents)
    : IQueryHandler<GetShipperTransportOrderQuery, Result<ShipperTransportOrderDetail>>
{
    public async ValueTask<Result<ShipperTransportOrderDetail>> Handle(GetShipperTransportOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notFound = ShipperTrackingRefused.Because<ShipperTransportOrderDetail>(ShipperTrackingRefusal.TransportOrderNotFound());
        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number))
        {
            return notFound;
        }

        var order = await transportOrders.FirstOrDefaultAsync(new ShipperTransportOrderByNumberSpec(new ShipperId(query.ShipperId), number), cancellationToken);
        return order is null
            ? notFound
            : await ShipperTransportOrderDetailProjection.ProjectAsync(order, shipments, trackingEvents, cancellationToken);
    }
}
