using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ConsigneeTracking.GetView;

public sealed class GetConsigneeViewHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    TimeProvider time)
    : IQueryHandler<GetConsigneeViewQuery, Result<ConsigneeView>>
{
    public async ValueTask<Result<ConsigneeView>> Handle(GetConsigneeViewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A malformed code is the same answer as an unknown one, so nothing tells a guess from a near miss.
        if (!TrackingCode.TryParse(query.TrackingCode, out var code)
            || await transportOrders.FirstOrDefaultAsync(new TransportOrderByTrackingCodeSpec(code!), cancellationToken) is not { Number: not null } order)
        {
            return ConsigneeTrackingRefused.NotFound<ConsigneeView>();
        }

        var shipment = (await shipments.ListAsync(new ShipmentsOfTransportOrdersSpec([order.Id]), cancellationToken)).FirstOrDefault();
        var events = shipment is null ? [] : await trackingEvents.ListAsync(new TrackingTimelineSpec(shipment.Id), cancellationToken);

        // Only progress counts, as for the Shipper: an exception stays with the Planner (ADR 0011).
        var progress = events.Where(trackingEvent => !trackingEvent.Type.IsException()).OrderBy(trackingEvent => trackingEvent.EventTime).ToList();

        var delivered = progress.Where(trackingEvent => trackingEvent.Type == TrackingEventType.Delivered).Select(trackingEvent => (DateTimeOffset?)trackingEvent.EventTime).Max();
        var lastProgress = progress.Select(trackingEvent => (DateTimeOffset?)trackingEvent.EventTime).Max();
        if (time.GetUtcNow() > TrackingCode.ExpiresAt(delivered, lastProgress, order.ReceivedAt))
        {
            return ConsigneeTrackingRefused.NotFound<ConsigneeView>();
        }

        var proofOfDelivery = shipment is null ? null : await proofsOfDelivery.FirstOrDefaultAsync(new CurrentProofOfDeliverySpec(shipment.Id), cancellationToken);
        var details = order.Details;
        var deliveryWindow = shipment is null ? details.Delivery?.Window : shipment.DeliveryWindowOf(details);
        return new ConsigneeView(
            order.Number!.Value,
            PlaceOf(details.Collection?.Address),
            PlaceOf(details.Delivery?.Address),
            deliveryWindow is null ? null : new ShipperWindowView(deliveryWindow.Earliest?.ToUniversalTime(), deliveryWindow.Latest?.ToUniversalTime()),
            TrackingStatuses.Of(shipment?.Status),
            ShipmentEta.Of(progress, deliveryWindow)?.ToUniversalTime(),
            progress.MaxBy(trackingEvent => trackingEvent.EventTime)?.EventTime.ToUniversalTime(),
            [.. progress.Select(trackingEvent => new ShipperProgressEntry(
                trackingEvent.Type.ToString(),
                trackingEvent.EventTime.ToUniversalTime(),
                new ShipperPlaceView(trackingEvent.Location.Place, trackingEvent.Location.CountryCode)))],
            proofOfDelivery is null
                ? null
                : new ConsigneeProofOfDelivery(proofOfDelivery.ReceivedBy, proofOfDelivery.DeliveryTime.ToUniversalTime(), proofOfDelivery.Condition));
    }

    private static ShipperPlaceView? PlaceOf(Address? address) => address is null ? null : new(address.City, address.Country);
}
