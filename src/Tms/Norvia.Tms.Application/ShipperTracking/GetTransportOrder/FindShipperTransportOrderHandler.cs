using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

public sealed class FindShipperTransportOrderHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery)
    : IQueryHandler<FindShipperTransportOrderQuery, Result<ShipperTransportOrderDetail>>
{
    public async ValueTask<Result<ShipperTransportOrderDetail>> Handle(FindShipperTransportOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = string.IsNullOrWhiteSpace(query.ShipperReference)
            ? null
            : await transportOrders.FirstOrDefaultAsync(new ShipperTransportOrderByReferenceSpec(new ShipperId(query.ShipperId), query.ShipperReference), cancellationToken);
        return order is null
            ? ShipperTrackingRefused.Because<ShipperTransportOrderDetail>(ShipperTrackingRefusal.TransportOrderNotFound())
            : await ShipperTransportOrderDetailProjection.ProjectAsync(order, shipments, trackingEvents, proofsOfDelivery, cancellationToken);
    }
}
