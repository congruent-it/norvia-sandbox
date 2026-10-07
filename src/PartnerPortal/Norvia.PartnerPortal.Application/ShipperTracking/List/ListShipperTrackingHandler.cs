using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.List;

public sealed class ListShipperTrackingHandler(IPersonaDirectory personas, INorviaPartnerApi partnerApi)
    : IQueryHandler<ListShipperTrackingQuery, TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>>
{
    public async ValueTask<TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>> Handle(ListShipperTrackingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ShipperTrackingHandling.ShipperOf(personas, query.PersonaId) is { } shipper
            ? await partnerApi.ListTransportOrdersAsync(shipper, query.Filter, cancellationToken)
            : ShipperTrackingHandling.NoShipper<IReadOnlyList<ShipperTrackingSummary>>(query.PersonaId);
    }
}
