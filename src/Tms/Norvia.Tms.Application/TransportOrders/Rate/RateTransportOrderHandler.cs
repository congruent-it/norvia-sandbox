using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

public sealed class RateTransportOrderHandler(IRepository<TransportOrder> repository, IRateCardDirectory rateCards, TimeProvider time)
    : ICommandHandler<RateTransportOrderCommand, Result<RatingResult>>
{
    public async ValueTask<Result<RatingResult>> Handle(RateTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Only an Accepted Transport Order has a number, and every Accepted one has a rating result.
        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await repository.SingleOrDefaultAsync(new TransportOrderToRateSpec(number), cancellationToken) is not { RatingResult: { } } order)
        {
            return Result.NotFound();
        }

        // The Freight Charge, the rating result and TransportOrderRated's outbox message are saved in one transaction.
        if (order.Rate(rateCards.FindFor(order.ShipperId), time.GetUtcNow()))
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        return order.RatingResult.Value;
    }
}
