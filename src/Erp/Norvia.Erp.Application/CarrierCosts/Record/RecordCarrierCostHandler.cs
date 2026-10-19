using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Application.CarrierCosts.Record;

public sealed class RecordCarrierCostHandler(IRepository<CarrierCost> costs, IReadRepository<Carrier> carriers, TimeProvider time)
    : ICommandHandler<RecordCarrierCostCommand, Result<RecordCarrierCostResult>>
{
    public async ValueTask<Result<RecordCarrierCostResult>> Handle(RecordCarrierCostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var data = command.Cost;

        // Idempotent on the Carrier booking number: the same content again is the original Carrier cost, different content is a conflict.
        if (await costs.FirstOrDefaultAsync(new CarrierCostByBookingNumberSpec(data.CarrierBookingNumber), cancellationToken) is { } existing)
        {
            return existing.HasSameContentAs(data)
                ? new RecordCarrierCostResult(CarrierCostViews.Of(existing), Repeat: true)
                : CarrierCostRefused.Because<RecordCarrierCostResult>(CarrierCostRefusal.Conflict(data.CarrierBookingNumber));
        }

        if (await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(data.CarrierId), cancellationToken) is null)
        {
            return CarrierCostRefused.Because<RecordCarrierCostResult>(CarrierCostRefusal.CarrierNotFound(data.CarrierId.Value));
        }

        var (cost, refusal) = CarrierCost.Record(data, time.GetUtcNow());
        if (cost is null)
        {
            return CarrierCostRefused.Because<RecordCarrierCostResult>(
                refusal ?? throw new InvalidOperationException($"Carrier cost {data.CarrierBookingNumber} was neither recorded nor refused."));
        }

        await costs.AddAsync(cost, cancellationToken);
        return new RecordCarrierCostResult(CarrierCostViews.Of(cost), Repeat: false);
    }
}
