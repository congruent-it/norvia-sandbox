using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

public sealed class GetProofsOfDeliveryTimelineHandler(IReadRepository<Shipment> shipments, IReadRepository<ProofOfDelivery> proofsOfDelivery)
    : IQueryHandler<GetProofsOfDeliveryTimelineQuery, Result<IReadOnlyList<ProofOfDeliveryView>>>
{
    public async ValueTask<Result<IReadOnlyList<ProofOfDeliveryView>>> Handle(GetProofsOfDeliveryTimelineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!ShipmentNumber.TryParse(query.ShipmentNumber, out var number)
            || await shipments.FirstOrDefaultAsync(new ShipmentByNumberSpec(number), cancellationToken) is not { } shipment)
        {
            return Result.NotFound();
        }

        var proofs = await proofsOfDelivery.ListAsync(new ProofsOfDeliveryForShipmentSpec(shipment.Id), cancellationToken);
        return Result.Success<IReadOnlyList<ProofOfDeliveryView>>([.. proofs.Select(ProofOfDeliveryView.Of)]);
    }
}
