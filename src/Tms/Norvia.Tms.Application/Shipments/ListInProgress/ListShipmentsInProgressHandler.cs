using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Shipments.ListInProgress;

public sealed class ListShipmentsInProgressHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IReadRepository<ExceptionCase> exceptions,
    ICarrierDirectory carriers,
    ShipmentStaleRules staleRules,
    TimeProvider time)
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
        var shipmentIdsWithProof = (await proofsOfDelivery.ListAsync(new CurrentProofsOfDeliveryForShipmentsSpec([.. inProgress.Select(shipment => shipment.Id)]), cancellationToken)).ToHashSet();
        var shipmentIdsWithOpenException = (await exceptions.ListAsync(new OpenExceptionsForShipmentsSpec([.. inProgress.Select(shipment => shipment.Id)]), cancellationToken)).ToHashSet();
        var orderNumbers = inProgress.Select(shipment => shipment.TransportOrderNumber).Distinct().ToList();
        var detailsByOrder = (await transportOrders.ListAsync(new TransportOrdersByNumbersSpec(orderNumbers), cancellationToken))
            .ToDictionary(order => order.Number!, order => order.Details);

        var now = time.GetUtcNow();
        return [.. inProgress
            .Select(shipment => Summarize(shipment, [.. eventsByShipment[shipment.Id]], detailsByOrder.GetValueOrDefault(shipment.TransportOrderNumber), shipmentIdsWithProof.Contains(shipment.Id), shipmentIdsWithOpenException.Contains(shipment.Id), now))
            .OrderByDescending(summary => summary.IsStale)
            .ThenBy(summary => summary.Eta ?? DateTimeOffset.MaxValue)];
    }

    private ShipmentInProgressSummary Summarize(Shipment shipment, List<TrackingEvent> events, TransportOrderDetails? details, bool hasProofOfDelivery, bool hasOpenException, DateTimeOffset now)
    {
        var deliveryWindow = shipment.DeliveryWindowOf(details);
        var last = events.MaxBy(trackingEvent => trackingEvent.ReceivedAt);
        var isStale = staleRules.IsStale(shipment.Status, details?.Collection?.Window?.Latest, deliveryWindow?.Latest, last?.ReceivedAt, now);
        return new(
            shipment.Number.Value,
            CarriedByView.Of(shipment.CarriedBy, carriers),
            shipment.Status,
            last is null ? null : LastTrackingEventView.Of(last),
            ShipmentEta.Of(events, deliveryWindow),
            isStale,
            hasProofOfDelivery,
            hasOpenException);
    }
}
