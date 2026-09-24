using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.List;

public sealed class ListTransportOrdersHandler(IReadRepository<TransportOrder> repository, IShipperDirectory shippers)
    : IQueryHandler<ListTransportOrdersQuery, IReadOnlyList<TransportOrderSummary>>
{
    public async ValueTask<IReadOnlyList<TransportOrderSummary>> Handle(ListTransportOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orders = await repository.ListAsync(new TransportOrdersByStatusSpec(query.Status), cancellationToken);

        return [.. orders.Select(order => new TransportOrderSummary(
            order.Id.Value,
            order.Number?.Value,
            order.Status,
            ShipperView.Of(order.ShipperId, shippers),
            order.Details.ShipperReference,
            order.Details.Collection?.Window?.Earliest,
            order.Details.Collection?.Window?.Latest,
            order.ReceivedAt))];
    }
}
