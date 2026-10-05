using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListInProgress;

public sealed class ListShipmentsInProgressHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<TransportOrder> transportOrders,
    ICarrierDirectory carriers)
    : IQueryHandler<ListShipmentsInProgressQuery, IReadOnlyList<ShipmentInProgressSummary>>
{
    public async ValueTask<IReadOnlyList<ShipmentInProgressSummary>> Handle(ListShipmentsInProgressQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var inProgress = await shipments.ListAsync(new ShipmentsInProgressSpec(), cancellationToken);
        if (inProgress.Count == 0)
        {
            return [];
        }

        var eventsByShipment = (await trackingEvents.ListAsync(new TrackingEventsForShipmentsSpec([.. inProgress.Select(shipment => shipment.Id)]), cancellationToken))
            .ToLookup(trackingEvent => trackingEvent.ShipmentId);
        var orderNumbers = inProgress.Select(shipment => shipment.TransportOrderNumber).Distinct().ToList();
        var deliveryWindowByOrder = (await transportOrders.ListAsync(new TransportOrdersByNumbersSpec(orderNumbers), cancellationToken))
            .ToDictionary(order => order.Number!, order => order.Details.Delivery?.Window);

        return [.. inProgress.Select(shipment => Summarize(shipment, eventsByShipment[shipment.Id].ToList(), deliveryWindowByOrder.GetValueOrDefault(shipment.TransportOrderNumber)))];
    }

    private ShipmentInProgressSummary Summarize(Shipment shipment, List<TrackingEvent> events, TimeWindow? deliveryWindow)
    {
        var last = events.MaxBy(trackingEvent => trackingEvent.ReceivedAt);
        return new(
            shipment.Number.Value,
            CarriedByView.Of(shipment.CarriedBy, carriers),
            shipment.Status,
            last is null ? null : LastTrackingEventView.Of(last),
            ShipmentEta.Of(events, deliveryWindow));
    }
}
