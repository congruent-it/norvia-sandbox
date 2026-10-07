using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

public sealed class RateTransportOrderHandler(IRepository<TransportOrder> repository, IRateCardDirectory rateCards, BillingCheck billing, TimeProvider time)
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
        var now = time.GetUtcNow();
        var outcome = command.ReRateKey is { } key
            ? order.ReRate(rateCard, now, new IdempotencyKey(key))
            : order.Rate(rateCard, now) ? ReRateOutcome.ReRated : (ReRateOutcome?)null;
        switch (outcome)
        {
            case ReRateOutcome.ReRated:
                // Rating can complete the order's conditions to be Billable (P9), on the intake's Message trail as the rating
                // is. A Customer service re-rate starts in the TMS UI, so it gets a Message trail of its own. The rating result,
                // with its Freight Charge or failure, the order becoming Billable and the outcome events' outbox messages are
                // saved in one transaction.
                var billingTrail = command.ReRateKey is null ? order.CorrelationId : CorrelationId.New();
                await billing.CheckAsync(order, shipment: null, new BillingChange(billingTrail, now), cancellationToken);
                await repository.SaveChangesAsync(cancellationToken);
                break;
            case ReRateOutcome.Refused:
                // Refused on its intake's Message trail, as a re-rate is; a conflict can't carry a correlation ID.
                return Result.Error(new ErrorList(
                    [$"Transport Order {number} is {order.RatingResult}; only a Transport Order whose Rating failed can be re-rated."],
                    order.CorrelationId.Value));
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
