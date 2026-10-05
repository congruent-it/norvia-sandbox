using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.TrackingEvents.Timeline;

public sealed class GetTrackingTimelineHandler(IReadRepository<Shipment> shipments, IReadRepository<TrackingEvent> trackingEvents)
    : IQueryHandler<GetTrackingTimelineQuery, Result<IReadOnlyList<TrackingTimelineEntry>>>
{
    public async ValueTask<Result<IReadOnlyList<TrackingTimelineEntry>>> Handle(GetTrackingTimelineQuery query, CancellationToken cancellationToken)
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
        return Result.Success<IReadOnlyList<TrackingTimelineEntry>>([.. events.Select(TrackingTimelineEntry.Of)]);
    }
}
