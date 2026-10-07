using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.GetTransportOrder;

public sealed class GetShipperTrackingHandler(IPersonaDirectory personas, INorviaPartnerApi partnerApi)
    : IQueryHandler<GetShipperTrackingQuery, TrackingAnswer<ShipperTrackingDetail>>
{
    public async ValueTask<TrackingAnswer<ShipperTrackingDetail>> Handle(GetShipperTrackingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ShipperTrackingHandling.ShipperOf(personas, query.PersonaId) is { } shipper
            ? await partnerApi.GetTransportOrderAsync(shipper, query.TransportOrderNumber, cancellationToken)
            : ShipperTrackingHandling.NoShipper<ShipperTrackingDetail>(query.PersonaId);
    }
}
