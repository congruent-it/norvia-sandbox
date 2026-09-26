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
        ArgumentNullException.ThrowIfNull(query);

        var orders = await repository.ListAsync(new TransportOrdersRatingFailedSpec(), cancellationToken);

        return [.. orders.Select(Summarize)];
    }

    // Only an Accepted Transport Order is rated, so each has a number, and Rating failed always carries its failure. An
    // order that breaks this fails the list rather than dropping out of it unseen.
    private RatingExceptionSummary Summarize(TransportOrder order) => order is { Number: { } number, RatingFailure: { } failure, RatingFailedAt: { } failedAt }
        ? new(
            order.Id.Value,
            number.Value,
            ShipperView.Of(order.ShipperId, shippers),
            order.Details.ShipperReference,
            order.Details.Collection?.Window?.Earliest,
            order.ReceivedAt,
            RatingFailureView.From(failure)!,
            failedAt)
        : throw new InvalidOperationException($"Transport Order {order.Id} is Rating failed without a number or a failure.");
}
