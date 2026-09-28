using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.ToPlan;

public sealed class ListTransportOrdersToPlanHandler(IReadRepository<TransportOrder> repository, IShipperDirectory shippers)
    : IQueryHandler<ListTransportOrdersToPlanQuery, IReadOnlyList<TransportOrderToPlanSummary>>
{
    public async ValueTask<IReadOnlyList<TransportOrderToPlanSummary>> Handle(ListTransportOrdersToPlanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // An Accepted Transport Order isn't Planned yet: planning moves it on to Planned.
        var orders = await repository.ListAsync(new TransportOrdersByStatusSpec(TransportOrderStatus.Accepted), cancellationToken);

        // Sorted here, not in the database: the collection window is stored with what the Shipper submitted, as JSON.
        // The spec gives them in the order received, which breaks a tie. The Transport Order rules make every Accepted
        // order give its collection window, so none sorts as missing one.
        return [.. orders
            .OrderBy(order => order.Details.Collection?.Window?.Earliest ?? DateTimeOffset.MaxValue)
            .ThenBy(order => order.ReceivedAt)
            .Select(Summarize)];
    }

    // An Accepted Transport Order always has a number and a rating result. One that breaks this fails the list rather
    // than dropping out of it unseen.
    private TransportOrderToPlanSummary Summarize(TransportOrder order) => order is { Number: { } number, RatingResult: { } ratingResult }
        ? new(
            number.Value,
            ratingResult,
            ShipperView.Of(order.ShipperId, shippers),
            order.Details.ShipperReference,
            PlaceView.Of(order.Details.Collection?.Address),
            PlaceView.Of(order.Details.Delivery?.Address),
            order.Details.Collection?.Window?.Earliest,
            order.Details.Collection?.Window?.Latest,
            order.ReceivedAt)
        : throw new InvalidOperationException($"Transport Order {order.Id} is Accepted without a number or a rating result.");
}
