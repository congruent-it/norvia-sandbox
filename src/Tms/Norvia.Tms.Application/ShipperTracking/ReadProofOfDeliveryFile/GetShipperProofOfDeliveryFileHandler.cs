using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.ShipperTracking.ReadProofOfDeliveryFile;

public sealed class GetShipperProofOfDeliveryFileHandler(
    IReadRepository<TransportOrder> transportOrders,
    IReadRepository<Shipment> shipments,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IProofOfDeliveryFiles files)
    : IQueryHandler<GetShipperProofOfDeliveryFileQuery, Result<ProofOfDeliveryFileContent>>
{
    public async ValueTask<Result<ProofOfDeliveryFileContent>> Handle(GetShipperProofOfDeliveryFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TransportOrderNumber.TryParse(query.TransportOrderNumber, out var number)
            || await transportOrders.FirstOrDefaultAsync(new ShipperTransportOrderByNumberSpec(new ShipperId(query.ShipperId), number), cancellationToken) is not { } order)
        {
            return ShipperTrackingRefused.Because<ProofOfDeliveryFileContent>(ShipperTrackingRefusal.TransportOrderNotFound());
        }

        // Only the current Proof of delivery's files are the Shipper's: a superseded one's are found nowhere.
        if ((await shipments.ListAsync(new ShipmentsOfTransportOrdersSpec([order.Id]), cancellationToken)).FirstOrDefault() is not { } shipment
            || await proofsOfDelivery.FirstOrDefaultAsync(new CurrentProofOfDeliverySpec(shipment.Id), cancellationToken) is not { } proofOfDelivery
            || proofOfDelivery.Files.FirstOrDefault(file => file.Id == query.FileId) is not { } file
            || await files.OpenReadAsync(shipment.Number, file, cancellationToken) is not { } content)
        {
            return ShipperTrackingRefused.Because<ProofOfDeliveryFileContent>(ShipperTrackingRefusal.ProofOfDeliveryNotFound());
        }

        return new ProofOfDeliveryFileContent(file.Name, file.ContentType, content);
    }
}
