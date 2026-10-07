using Ardalis.SharedKernel;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ShipperTracking.GetTransportOrder;

/// <summary>What the Shipper sees of one Transport Order, the same whether it was got by number or found by reference.</summary>
internal static class ShipperTransportOrderDetailProjection
{
    public static async Task<ShipperTransportOrderDetail> ProjectAsync(
        TransportOrder order,
        IReadRepository<Shipment> shipments,
        IReadRepository<TrackingEvent> trackingEvents,
        IReadRepository<ProofOfDelivery> proofsOfDelivery,
        CancellationToken cancellationToken)
    {
        var shipment = (await shipments.ListAsync(new ShipmentsOfTransportOrdersSpec([order.Id]), cancellationToken)).FirstOrDefault();
        var events = shipment is null ? [] : await trackingEvents.ListAsync(new TrackingTimelineSpec(shipment.Id), cancellationToken);

        var proofOfDelivery = shipment is null ? null : await proofsOfDelivery.FirstOrDefaultAsync(new CurrentProofOfDeliverySpec(shipment.Id), cancellationToken);

        var details = order.Details;
        var deliveryWindow = shipment is null ? details.Delivery?.Window : shipment.DeliveryWindowOf(details);
        // Only progress counts, for the update, the ETA and the progress alike: an exception stays with the Planner (ADR 0011).
        var progress = events.Where(trackingEvent => !trackingEvent.Type.IsException()).OrderBy(trackingEvent => trackingEvent.EventTime).ToList();
        return new ShipperTransportOrderDetail(
            order.Number!.Value,
            order.ShipperReference,
            PlaceOf(details.Collection?.Address),
            PlaceOf(details.Delivery?.Address),
            WindowOf(details.Collection?.Window),
            WindowOf(deliveryWindow),
            TrackingStatuses.Of(shipment?.Status),
            ShipmentEta.Of(progress, deliveryWindow),
            progress.MaxBy(trackingEvent => trackingEvent.EventTime)?.EventTime,
            [.. progress.Select(trackingEvent => new ShipperProgressEntry(
                trackingEvent.Type.ToString(),
                trackingEvent.EventTime,
                new ShipperPlaceView(trackingEvent.Location.Place, trackingEvent.Location.CountryCode)))],
            proofOfDelivery is null
                ? null
                : new ShipperProofOfDelivery(
                    proofOfDelivery.ReceivedBy,
                    proofOfDelivery.DeliveryTime,
                    proofOfDelivery.Condition,
                    [.. proofOfDelivery.Files.Select(file => new ShipperProofOfDeliveryFile(file.Id, file.Name, file.ContentType, file.SizeBytes))]));
    }

    private static ShipperPlaceView? PlaceOf(Address? address) => address is null ? null : new(address.City, address.Country);

    private static ShipperWindowView? WindowOf(TimeWindow? window) => window is null ? null : new(window.Earliest, window.Latest);
}
