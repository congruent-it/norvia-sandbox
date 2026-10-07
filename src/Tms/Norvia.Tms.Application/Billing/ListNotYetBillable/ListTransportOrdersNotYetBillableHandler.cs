using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Billing.ListNotYetBillable;

public sealed class ListTransportOrdersNotYetBillableHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IReadRepository<ExceptionCase> exceptions,
    IShipperDirectory shippers)
    : IQueryHandler<ListTransportOrdersNotYetBillableQuery, IReadOnlyList<TransportOrderNotYetBillableSummary>>
{
    public async ValueTask<IReadOnlyList<TransportOrderNotYetBillableSummary>> Handle(
        ListTransportOrdersNotYetBillableQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orders = await transportOrders.ListAsync(new TransportOrdersNotYetBillableSpec(), cancellationToken);
        if (orders.Count == 0)
        {
            return [];
        }

        var delivered = await shipments.ListAsync(
            new DeliveredShipmentsOfOrdersSpec([.. orders.Select(order => order.Number!)]), cancellationToken);
        if (delivered.Count == 0)
        {
            return [];
        }

        var ids = delivered.Select(shipment => shipment.Id).ToList();
        var shipmentsByOrder = delivered.ToDictionary(shipment => shipment.TransportOrderNumber);
        var deliveredAt = (await trackingEvents.ListAsync(new DeliveredTrackingEventsForShipmentsSpec(ids), cancellationToken))
            .GroupBy(row => row.ShipmentId)
            .ToDictionary(rows => rows.Key, rows => rows.Min(row => row.EventTime));
        var withProof = (await proofsOfDelivery.ListAsync(new CurrentProofsOfDeliveryForShipmentsSpec(ids), cancellationToken)).ToHashSet();
        var withException = (await exceptions.ListAsync(new OpenExceptionsForShipmentsSpec(ids), cancellationToken)).ToHashSet();

        return [.. orders
            .Where(order => shipmentsByOrder.ContainsKey(order.Number!))
            .Select(order => Summarize(order, shipmentsByOrder[order.Number!], deliveredAt, withProof, withException))
            .OrderBy(summary => summary.DeliveredAt)
            .ThenBy(summary => summary.TransportOrderNumber, StringComparer.Ordinal)];
    }

    private TransportOrderNotYetBillableSummary Summarize(
        TransportOrder order,
        DeliveredShipmentOfOrderRow shipment,
        Dictionary<ShipmentId, DateTimeOffset> deliveredAt,
        HashSet<ShipmentId> withProof,
        HashSet<ShipmentId> withException) =>
        new(
            order.Number!.Value,
            shipment.Number.Value,
            ShipperView.Of(order.ShipperId, shippers),
            order.Details.ShipperReference,
            deliveredAt.TryGetValue(shipment.Id, out var at)
                ? at
                : throw new InvalidOperationException($"Shipment {shipment.Number} is Delivered, but no Delivered Tracking event moved it there."),
            BlockingReasons(order, shipment.Id, withProof, withException));

    private static List<BillingBlockingReason> BlockingReasons(
        TransportOrder order, ShipmentId shipmentId, HashSet<ShipmentId> withProof, HashSet<ShipmentId> withException)
    {
        var reasons = new List<BillingBlockingReason>();
        if (order.RatingResult != RatingResult.Rated)
        {
            reasons.Add(BillingBlockingReason.NotRated);
        }

        if (!withProof.Contains(shipmentId))
        {
            reasons.Add(BillingBlockingReason.NoProofOfDelivery);
        }

        if (withException.Contains(shipmentId))
        {
            reasons.Add(BillingBlockingReason.OpenException);
        }

        return reasons;
    }
}
