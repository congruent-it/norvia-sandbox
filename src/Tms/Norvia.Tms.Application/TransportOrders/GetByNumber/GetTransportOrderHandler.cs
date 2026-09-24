using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
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
        return order is null ? Result.NotFound() : TransportOrderView.Of(order, shippers);
    }
}
