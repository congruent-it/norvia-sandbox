using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

public sealed class GetProofOfDeliveryHandler(IReadRepository<Shipment> shipments, IReadRepository<ProofOfDelivery> proofsOfDelivery)
    : IQueryHandler<GetProofOfDeliveryQuery, Result<ProofOfDeliveryView>>
{
    public async ValueTask<Result<ProofOfDeliveryView>> Handle(GetProofOfDeliveryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ShipmentNumber.TryParse(query.ShipmentNumber, out var number)
            || await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(number), cancellationToken) is not { } shipment
            || await proofsOfDelivery.SingleOrDefaultAsync(new CurrentProofOfDeliverySpec(shipment.Id), cancellationToken) is not { } proofOfDelivery)
        {
            return Result.NotFound();
        }

        return ProofOfDeliveryView.Of(proofOfDelivery);
    }
}
