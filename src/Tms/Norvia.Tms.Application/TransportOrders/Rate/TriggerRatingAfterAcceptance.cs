using Ardalis.Result;
using Mediator;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Triggers the rating worker once a submission has been answered as Accepted. The submission has committed by then, so
/// the worker finds the new Transport Order Not yet rated.
/// </summary>
public sealed class TriggerRatingAfterAcceptance(IRatingTrigger trigger)
    : IPipelineBehavior<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>>
{
    public async ValueTask<Result<SubmitTransportOrderResult>> Handle(
        SubmitTransportOrderCommand message,
        MessageHandlerDelegate<SubmitTransportOrderCommand, Result<SubmitTransportOrderResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var result = await next(message, cancellationToken);
        if (result.IsSuccess && result.Value.Status == TransportOrderStatus.Accepted)
        {
            trigger.Trigger();
        }

        return result;
    }
}
