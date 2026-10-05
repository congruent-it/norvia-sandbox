using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.ProofsOfDelivery.ListMissing;

public sealed class ListDeliveredShipmentsWithoutProofOfDeliveryHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    ICarrierDirectory carriers,
    ProofOfDeliveryMissingRules rules,
    TimeProvider time)
    : IQueryHandler<ListDeliveredShipmentsWithoutProofOfDeliveryQuery, IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>>
{
    public async ValueTask<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>> Handle(
        ListDeliveredShipmentsWithoutProofOfDeliveryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var delivered = await shipments.ListAsync(new DeliveredShipmentsSpec(), cancellationToken);
        if (delivered.Count == 0)
        {
            return [];
        }

        var ids = delivered.Select(shipment => shipment.Id).ToList();
        var withProof = (await proofsOfDelivery.ListAsync(new CurrentProofsOfDeliveryForShipmentsSpec(ids), cancellationToken)).ToHashSet();
        var deliveredAtByShipment = (await trackingEvents.ListAsync(new DeliveredTrackingEventsForShipmentsSpec(ids), cancellationToken))
            .GroupBy(row => row.ShipmentId)
            .ToDictionary(rows => rows.Key, rows => rows.Min(row => row.EventTime));

        var now = time.GetUtcNow();
        return [.. delivered
            .Where(shipment => !withProof.Contains(shipment.Id))
            .Select(shipment => Summarize(shipment, DeliveredAt(shipment, deliveredAtByShipment), now))
            .OrderByDescending(summary => summary.IsProofOfDeliveryMissing)
            .ThenBy(summary => summary.DeliveredAt)
            .ThenBy(summary => summary.ShipmentNumber, StringComparer.Ordinal)];
    }

    private DeliveredShipmentWithoutProofOfDeliverySummary Summarize(DeliveredShipmentRow shipment, DateTimeOffset deliveredAt, DateTimeOffset now) =>
        new(shipment.Number.Value, CarriedByView.Of(shipment.CarriedBy, carriers), deliveredAt, rules.IsMissing(deliveredAt, now));

    private static DateTimeOffset DeliveredAt(DeliveredShipmentRow shipment, Dictionary<ShipmentId, DateTimeOffset> deliveredAtByShipment) =>
        deliveredAtByShipment.TryGetValue(shipment.Id, out var deliveredAt)
            ? deliveredAt
            : throw new InvalidOperationException($"Shipment {shipment.Number} is Delivered, but no Delivered Tracking event moved it there.");
}
