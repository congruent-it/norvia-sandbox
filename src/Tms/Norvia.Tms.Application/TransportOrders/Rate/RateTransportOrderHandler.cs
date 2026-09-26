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
        var outcome = command.ReRateKey is { } key
            ? order.ReRate(rateCard, time.GetUtcNow(), new IdempotencyKey(key))
            : order.Rate(rateCard, time.GetUtcNow()) ? ReRateOutcome.ReRated : (ReRateOutcome?)null;
        switch (outcome)
        {
            case ReRateOutcome.ReRated:
                // The rating result, with its Freight Charge or failure, and the outcome event's outbox message are
                // saved in one transaction.
                await repository.SaveChangesAsync(cancellationToken);
                break;
            case ReRateOutcome.Refused:
                return Result.Conflict(
                    $"Transport Order {number} is {order.RatingResult}; only a Transport Order whose Rating failed can be re-rated.");
        }

        // A first rating of an order that is no longer Not yet rated changes nothing, and answers with where it stands.
        return new RateTransportOrderResult(
            order.RatingResult.Value,
            RatingFailureView.From(order.RatingFailure),
            order.ShipperId.Value,
            order.CorrelationId.Value,
            Repeat: outcome == ReRateOutcome.Repeat);
    }
}
