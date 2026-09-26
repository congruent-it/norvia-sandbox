using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

public sealed class ListTransportOrdersNotYetRatedHandler(IReadRepository<TransportOrder> repository)
    : IQueryHandler<ListTransportOrdersNotYetRatedQuery, IReadOnlyList<TransportOrderToRate>>
{
    public async ValueTask<IReadOnlyList<TransportOrderToRate>> Handle(ListTransportOrdersNotYetRatedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orders = await repository.ListAsync(new TransportOrdersNotYetRatedSpec(query.Skip, query.BatchSize), cancellationToken);

        // Only an Accepted Transport Order waits for Rating, and every one has a number.
        return [.. orders.Where(order => order.Number is not null).Select(order => new TransportOrderToRate(
            order.Number!.Value,
            order.ShipperId.Value,
            order.CorrelationId.Value))];
    }
}
