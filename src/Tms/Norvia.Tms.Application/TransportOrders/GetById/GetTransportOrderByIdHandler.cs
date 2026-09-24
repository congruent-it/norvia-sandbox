using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.GetById;

public sealed class GetTransportOrderByIdHandler(IReadRepository<TransportOrder> repository, IShipperDirectory shippers)
    : IQueryHandler<GetTransportOrderByIdQuery, Result<TransportOrderView>>
{
    public async ValueTask<Result<TransportOrderView>> Handle(GetTransportOrderByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await repository.FirstOrDefaultAsync(new TransportOrderByIdSpec(new TransportOrderId(query.Id)), cancellationToken);
        return order is null ? Result.NotFound() : TransportOrderView.Of(order, shippers);
    }
}
