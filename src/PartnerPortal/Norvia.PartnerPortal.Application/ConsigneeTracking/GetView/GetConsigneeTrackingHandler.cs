using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.ShipperTracking;

namespace Norvia.PartnerPortal.Application.ConsigneeTracking.GetView;

public sealed class GetConsigneeTrackingHandler(INorviaPartnerApi partnerApi)
    : IQueryHandler<GetConsigneeTrackingQuery, TrackingAnswer<ConsigneeTrackingView>>
{
    public async ValueTask<TrackingAnswer<ConsigneeTrackingView>> Handle(GetConsigneeTrackingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A link that carries nothing usable is as good as a wrong code, and Norvia isn't asked.
        return string.IsNullOrWhiteSpace(query.TrackingCode)
            ? TrackingAnswer.NotFound<ConsigneeTrackingView>("We can't find this shipment.")
            : await partnerApi.GetConsigneeViewAsync(query.TrackingCode.Trim(), query.ClientIp, cancellationToken);
    }
}
