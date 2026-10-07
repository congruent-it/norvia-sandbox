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

        return await ShipperTrackingHandling.AskAsShipperAsync(personas, query.PersonaId, shipper => partnerApi.ListTransportOrdersAsync(shipper, query.Filter, cancellationToken));
    }
}
