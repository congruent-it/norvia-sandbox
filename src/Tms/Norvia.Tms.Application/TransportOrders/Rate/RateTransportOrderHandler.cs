using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

public sealed class RateTransportOrderHandler(IRepository<TransportOrder> repository, IRateCardDirectory rateCards, TimeProvider time)
    : ICommandHandler<RateTransportOrderCommand, Result<RateTransportOrderResult>>
{
    public async ValueTask<Result<RateTransportOrderResult>> Handle(RateTransportOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Only an Accepted Transport Order has a number, and every Accepted one has a rating result.
        if (!TransportOrderNumber.TryParse(command.TransportOrderNumber, out var number)
            || await repository.SingleOrDefaultAsync(new TransportOrderToRateSpec(number), cancellationToken) is not { RatingResult: { } } order)
        {
            return Result.NotFound();
        }

        var rateCard = rateCards.FindFor(order.ShipperId);
        var rated = command.ReRate ? order.ReRate(rateCard, time.GetUtcNow()) : order.Rate(rateCard, time.GetUtcNow());
        if (rated)
        {
            // The rating result, with its Freight Charge or failure, and the outcome event's outbox message are saved in
            // one transaction.
            await repository.SaveChangesAsync(cancellationToken);
        }
        else if (command.ReRate)
        {
            return Result.Conflict(
                $"Transport Order {number} is {order.RatingResult}; only a Transport Order whose Rating failed can be re-rated.");
        }

        return new RateTransportOrderResult(
            order.RatingResult.Value,
            RatingFailureView.From(order.RatingFailure),
            order.ShipperId.Value,
            order.CorrelationId.Value);
    }
}
