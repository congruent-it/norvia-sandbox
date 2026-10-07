using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;

namespace Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;

public sealed class DownloadProofOfDeliveryFileHandler(IPersonaDirectory personas, INorviaPartnerApi partnerApi)
    : IQueryHandler<DownloadProofOfDeliveryFileQuery, TrackingAnswer<ShipperProofFileContent>>
{
    public async ValueTask<TrackingAnswer<ShipperProofFileContent>> Handle(DownloadProofOfDeliveryFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ShipperTrackingHandling.ShipperOf(personas, query.PersonaId) is { } shipper
            ? await partnerApi.DownloadProofOfDeliveryFileAsync(shipper, query.TransportOrderNumber, query.FileId, cancellationToken)
            : ShipperTrackingHandling.NoShipper<ShipperProofFileContent>(query.PersonaId);
    }
}
