using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

public sealed class GetProofOfDeliveryFileHandler(
    IReadRepository<Shipment> shipments,
    IReadRepository<ProofOfDelivery> proofsOfDelivery,
    IProofOfDeliveryFiles files)
    : IQueryHandler<GetProofOfDeliveryFileQuery, Result<ProofOfDeliveryFileContent>>
{
    public async ValueTask<Result<ProofOfDeliveryFileContent>> Handle(GetProofOfDeliveryFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ShipmentNumber.TryParse(query.ShipmentNumber, out var number)
            || await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(number), cancellationToken) is not { } shipment
            || (await proofsOfDelivery.ListAsync(new ProofsOfDeliveryForShipmentSpec(shipment.Id), cancellationToken))
                .SelectMany(proofOfDelivery => proofOfDelivery.Files)
                .FirstOrDefault(file => file.Id == query.FileId) is not { } file)
        {
            return Result.NotFound();
        }

        // The Proof of delivery and its file list outlive the content: the retention period deletes only the blob.
        if (await files.OpenReadAsync(shipment.Number, file, cancellationToken) is not { } content)
        {
            return ProofOfDeliveryFileExpiry.Expired();
        }

        return new ProofOfDeliveryFileContent(file.Name, file.ContentType, content);
    }
}
