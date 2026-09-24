using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.GetByNumber;

public sealed class GetTransportOrderHandler(IReadRepository<TransportOrder> repository, IShipperDirectory shippers)
    : IQueryHandler<GetTransportOrderQuery, Result<TransportOrderView>>
{
    public async ValueTask<Result<TransportOrderView>> Handle(GetTransportOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number))
        {
            return Result.NotFound();
        }

        var order = await repository.FirstOrDefaultAsync(new TransportOrderByNumberSpec(number), cancellationToken);
        if (order is null)
        {
            return Result.NotFound();
        }

        return new TransportOrderView(
            order.Number.Value,
            order.Status,
            ShipperView.Of(order.ShipperId, shippers),
            order.CorrelationId.Value,
            order.ReceivedAt,
            CanonicalTransportOrder.From(order));
    }
}
