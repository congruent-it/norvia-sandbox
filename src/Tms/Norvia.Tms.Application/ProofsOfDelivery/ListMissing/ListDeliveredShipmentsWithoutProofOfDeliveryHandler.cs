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
        var withProof = (await proofsOfDelivery.ListAsync(new CurrentProofsOfDeliveryForShipmentsSpec(ids), cancellationToken))
            .Select(proofOfDelivery => proofOfDelivery.ShipmentId)
            .ToHashSet();
        var deliveredEvents = (await trackingEvents.ListAsync(new DeliveredTrackingEventsForShipmentsSpec(ids), cancellationToken))
            .ToLookup(trackingEvent => trackingEvent.ShipmentId);

        var now = time.GetUtcNow();
        return [.. delivered
            .Where(shipment => !withProof.Contains(shipment.Id))
            .Select(shipment => Summarize(shipment, DeliveredAt(shipment, deliveredEvents[shipment.Id]), now))
            .OrderByDescending(summary => summary.IsProofOfDeliveryMissing)
            .ThenBy(summary => summary.DeliveredAt)];
    }

    private DeliveredShipmentWithoutProofOfDeliverySummary Summarize(Shipment shipment, DateTimeOffset deliveredAt, DateTimeOffset now) =>
        new(shipment.Number.Value, CarriedByView.Of(shipment.CarriedBy, carriers), deliveredAt, rules.IsMissing(deliveredAt, now));

    /// <summary>The event time of the Delivered event that made the Shipment Delivered; a later repeat of it doesn't move it.</summary>
    private static DateTimeOffset DeliveredAt(Shipment shipment, IEnumerable<TrackingEvent> deliveredEvents) =>
        deliveredEvents.Where(trackingEvent => trackingEvent.MovedShipment).Select(trackingEvent => (DateTimeOffset?)trackingEvent.EventTime).FirstOrDefault()
        ?? throw new InvalidOperationException($"Shipment {shipment.Number} is Delivered, but no Delivered Tracking event moved it there.");
}
