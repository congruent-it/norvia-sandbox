using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ShipperTracking.ListTransportOrders;

public sealed class ListShipperTransportOrdersHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents)
    : IQueryHandler<ListShipperTransportOrdersQuery, IReadOnlyList<ShipperTransportOrderListItem>>
{
    public async ValueTask<IReadOnlyList<ShipperTransportOrderListItem>> Handle(ListShipperTransportOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orders = await transportOrders.ListAsync(new ShipperTransportOrdersSpec(new ShipperId(query.ShipperId)), cancellationToken);
        if (orders.Count == 0)
        {
            return [];
        }

        var shipmentByOrder = (await shipments.ListAsync(new ShipmentsOfTransportOrdersSpec([.. orders.Select(order => order.Id)]), cancellationToken))
            .ToDictionary(shipment => shipment.TransportOrderId);
        var eventsByShipment = (await trackingEvents.ListAsync(new TrackingEventsForShipmentsSpec([.. shipmentByOrder.Values.Select(shipment => shipment.Id)]), cancellationToken))
            .ToLookup(trackingEvent => trackingEvent.ShipmentId);

        // The collection date is inside the Transport Order's details document, so the list is ordered here.
        return [.. orders
            .Select(order =>
            {
                var shipment = shipmentByOrder.GetValueOrDefault(order.Id);
                return Project(order, shipment, shipment is null ? [] : [.. eventsByShipment[shipment.Id]]);
            })
            .OrderByDescending(item => item.CollectionEarliest)
            .Select(item => item.Item)];
    }

    private static (ShipperTransportOrderListItem Item, DateTimeOffset? CollectionEarliest) Project(TransportOrder order, Shipment? shipment, List<TrackingEvent> events)
    {
        var details = order.Details;
        var deliveryWindow = shipment is null ? details.Delivery?.Window : shipment.DeliveryWindowOf(details);
        // Only progress counts, for the update and the ETA alike: an exception stays with the Planner (ADR 0011).
        var progress = events.Where(trackingEvent => !trackingEvent.Type.IsException()).ToList();
        var lastUpdate = progress.MaxBy(trackingEvent => trackingEvent.EventTime)?.EventTime;
        var item = new ShipperTransportOrderListItem(
            order.Number!.Value,
            order.ShipperReference,
            PlaceOf(details.Collection?.Address),
            PlaceOf(details.Delivery?.Address),
            TrackingStatuses.Of(shipment?.Status),
            ShipmentEta.Of(progress, deliveryWindow),
            lastUpdate);
        return (item, details.Collection?.Window?.Earliest);
    }

    private static ShipperPlaceView? PlaceOf(Address? address) => address is null ? null : new(address.City, address.Country);
}
