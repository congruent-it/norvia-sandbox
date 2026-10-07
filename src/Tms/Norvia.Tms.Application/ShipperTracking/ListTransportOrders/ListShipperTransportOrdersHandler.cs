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

        // The collection date is inside the Transport Order's details document and the Tracking status is worked out, so the
        // filters, the order and the page are applied here. Only the page's Shipments have their Tracking events read.
        var pageOfOrders = orders
            .Where(order => Matches(query, order, shipmentByOrder.GetValueOrDefault(order.Id)))
            .OrderByDescending(order => order.Details.Collection?.Window?.Earliest)
            .ThenByDescending(order => order.Number!.Value, StringComparer.Ordinal)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();
        if (pageOfOrders.Count == 0)
        {
            return [];
        }

        var shipmentIds = pageOfOrders.Select(order => shipmentByOrder.GetValueOrDefault(order.Id)).OfType<Shipment>().Select(shipment => shipment.Id).ToList();
        var eventsByShipment = (await trackingEvents.ListAsync(new TrackingEventsForShipmentsSpec(shipmentIds), cancellationToken))
            .ToLookup(trackingEvent => trackingEvent.ShipmentId);

        return [.. pageOfOrders.Select(order =>
        {
            var shipment = shipmentByOrder.GetValueOrDefault(order.Id);
            return Project(order, shipment, shipment is null ? [] : [.. eventsByShipment[shipment.Id]]);
        })];
    }

    private static bool Matches(ListShipperTransportOrdersQuery query, TransportOrder order, Shipment? shipment)
    {
        if (query.TrackingStatus is { } status && TrackingStatuses.Of(shipment?.Status) != status)
        {
            return false;
        }

        if (query.CollectionFrom is null && query.CollectionTo is null)
        {
            return true;
        }

        // The date is the one at the collection place, so the window's own offset decides it.
        if (order.Details.Collection?.Window?.Earliest is not { } earliest)
        {
            return false;
        }

        var date = DateOnly.FromDateTime(earliest.DateTime);
        return (query.CollectionFrom is null || date >= query.CollectionFrom) && (query.CollectionTo is null || date <= query.CollectionTo);
    }

    private static ShipperTransportOrderListItem Project(TransportOrder order, Shipment? shipment, List<TrackingEvent> events)
    {
        var details = order.Details;
        var deliveryWindow = shipment is null ? details.Delivery?.Window : shipment.DeliveryWindowOf(details);
        // Only progress counts, for the update and the ETA alike: an exception stays with the Planner (ADR 0011).
        var progress = events.Where(trackingEvent => !trackingEvent.Type.IsException()).ToList();
        var lastUpdate = progress.MaxBy(trackingEvent => trackingEvent.EventTime)?.EventTime;
        return new ShipperTransportOrderListItem(
            order.Number!.Value,
            order.ShipperReference,
            PlaceOf(details.Collection?.Address),
            PlaceOf(details.Delivery?.Address),
            TrackingStatuses.Of(shipment?.Status),
            ShipmentEta.Of(progress, deliveryWindow)?.ToUniversalTime(),
            lastUpdate?.ToUniversalTime());
    }

    private static ShipperPlaceView? PlaceOf(Address? address) => address is null ? null : new(address.City, address.Country);
}
