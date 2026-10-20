using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Browse;

public sealed class ListAllTransportOrdersHandler(
    IReadRepository<TransportOrder> transportOrders, IReadRepository<Shipment> shipments, IShipperDirectory shippers, ICarrierDirectory carriers)
    : IQueryHandler<ListAllTransportOrdersQuery, TransportOrderPage>
{
    public async ValueTask<TransportOrderPage> Handle(ListAllTransportOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, ListAllTransportOrdersQuery.MaxPageSize);
        var shipperId = string.IsNullOrWhiteSpace(query.ShipperId) ? null : new ShipperId(query.ShipperId);
        var carriedBy = CarriedByOf(query.CarriedBy);
        var onlyTransportOrders = carriedBy is null ? null : await TransportOrdersCarriedByAsync(carriedBy, cancellationToken);
        var spec = new TransportOrdersSearchSpec(query.Status, shipperId, query.Search, page, pageSize, onlyTransportOrders);

        // The database filters, searches, orders and pages; the count ignores the paging.
        var orders = await transportOrders.ListAsync(spec, cancellationToken);
        var totalCount = await transportOrders.CountAsync(spec, cancellationToken);
        if (orders.Count == 0)
        {
            return new([], totalCount, page, pageSize);
        }

        var shipmentByOrder = (await shipments.ListAsync(new ShipmentsOfTransportOrdersSpec([.. orders.Select(order => order.Id)]), cancellationToken))
            .ToDictionary(shipment => shipment.TransportOrderId);

        return new([.. orders.Select(order => Item(order, shipmentByOrder.GetValueOrDefault(order.Id)))], totalCount, page, pageSize);
    }

    private static CarriedBy? CarriedByOf(string? carriedBy) =>
        string.IsNullOrWhiteSpace(carriedBy) ? null
        : carriedBy == ListAllTransportOrdersQuery.OwnFleet ? Domain.Shipments.CarriedBy.OwnFleet
        : Domain.Shipments.CarriedBy.Carrier(new CarrierId(carriedBy));

    // A Shipment belongs to its own aggregate, so the orders it carries are found there and handed to the order search.
    private async Task<IReadOnlyCollection<TransportOrderId>> TransportOrdersCarriedByAsync(CarriedBy carriedBy, CancellationToken cancellationToken) =>
        [.. (await shipments.ListAsync(new ShipmentsCarriedBySpec(carriedBy), cancellationToken)).Select(shipment => shipment.TransportOrderId)];

    // The spec leaves out a Rejected booking, so every order here has its number; one that doesn't fails the list rather than dropping out unseen.
    private TransportOrderListItem Item(TransportOrder order, Shipment? shipment) => order.Number is { } number
        ? new(
            number.Value,
            order.Status,
            ShipperView.Of(order.ShipperId, shippers),
            order.ShipperReference,
            PlaceView.Of(order.Details.Collection?.Address),
            PlaceView.Of(order.Details.Delivery?.Address),
            order.Details.Collection?.Window?.Earliest,
            order.Details.Collection?.Window?.Latest,
            order.ShipmentNumber?.Value,
            shipment?.Status,
            shipment is null ? null : CarriedByView.Of(shipment.CarriedBy, carriers))
        : throw new InvalidOperationException($"Transport Order {order.Id} is listed without a number.");
}
