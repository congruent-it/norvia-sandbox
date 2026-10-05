using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

public sealed class GetTrackingTimelineHandler(IReadRepository<Shipment> shipments, IReadRepository<TrackingEvent> trackingEvents, IReadRepository<ProofOfDelivery> proofsOfDelivery)
    : IQueryHandler<GetTrackingTimelineQuery, Result<ShipmentTimeline>>
{
    public async ValueTask<Result<ShipmentTimeline>> Handle(GetTrackingTimelineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ShipmentNumber.TryParse(query.ShipmentNumber, out var number))
        {
            return Result.NotFound();
        }

        var shipment = await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(number), cancellationToken);
        if (shipment is null)
        {
            return Result.NotFound();
        }

        var events = await trackingEvents.ListAsync(new TrackingTimelineSpec(shipment.Id), cancellationToken);
        var proofs = await proofsOfDelivery.ListAsync(new ProofsOfDeliveryForShipmentSpec(shipment.Id), cancellationToken);
        return new ShipmentTimeline([.. events.Select(TrackingTimelineEntry.Of)], [.. proofs.Select(ProofOfDeliveryView.Of)]);
    }
}
