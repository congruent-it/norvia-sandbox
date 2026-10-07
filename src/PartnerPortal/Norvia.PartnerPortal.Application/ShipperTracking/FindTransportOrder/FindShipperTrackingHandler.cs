using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.FindTransportOrder;

public sealed class FindShipperTrackingHandler(IPersonaDirectory personas, INorviaPartnerApi partnerApi)
    : IQueryHandler<FindShipperTrackingQuery, TrackingAnswer<ShipperTrackingDetail>>
{
    public async ValueTask<TrackingAnswer<ShipperTrackingDetail>> Handle(FindShipperTrackingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ShipperTrackingHandling.ShipperOf(personas, query.PersonaId) is { } shipper
            ? await partnerApi.FindTransportOrderAsync(shipper, query.ShipperReference, cancellationToken)
            : ShipperTrackingHandling.NoShipper<ShipperTrackingDetail>(query.PersonaId);
    }
}
