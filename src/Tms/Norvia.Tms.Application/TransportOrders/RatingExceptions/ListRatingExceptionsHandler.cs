using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.RatingExceptions;

public sealed class ListRatingExceptionsHandler(IReadRepository<TransportOrder> repository, IShipperDirectory shippers)
    : IQueryHandler<ListRatingExceptionsQuery, IReadOnlyList<RatingExceptionSummary>>
{
    public async ValueTask<IReadOnlyList<RatingExceptionSummary>> Handle(ListRatingExceptionsQuery query, CancellationToken cancellationToken)
    {
        var orders = await repository.ListAsync(new TransportOrdersRatingFailedSpec(), cancellationToken);

        // Only an Accepted Transport Order is rated, so each has a number, and Rating failed always carries its failure.
        return [.. orders.Where(order => order is { Number: not null, RatingFailure: not null, RatingFailedAt: not null })
            .Select(order => new RatingExceptionSummary(
                order.Id.Value,
                order.Number!.Value,
                ShipperView.Of(order.ShipperId, shippers),
                order.Details.ShipperReference,
                order.Details.Collection?.Window?.Earliest,
                order.ReceivedAt,
                RatingFailureView.From(order.RatingFailure)!,
                order.RatingFailedAt!.Value))];
    }
}
