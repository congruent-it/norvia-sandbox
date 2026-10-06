using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Exceptions;

/// <summary>
/// Traces a Planner's action on an Exception, which starts in the TMS UI, on its own Message trail (coding guidelines §7): the
/// <c>correlationId</c> the TMS created for it for the whole call, and for its outcome <c>shipmentNumber</c>, in the logging
/// scope and on the request's trace. An action the TMS refuses records Refused with its <c>failureCode</c>. The API's request
/// scope carries <c>flow</c>. Each action says what it logs.
/// </summary>
public abstract class TrackExceptionActionInLoggingScope<TCommand, TResult> : IPipelineBehavior<TCommand, Result<TResult>>
    where TCommand : IMessage
{
    public async ValueTask<Result<TResult>> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, Result<TResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = Logger.Track(new() { [TrackingKeys.CorrelationId] = CorrelationIdOf(message) });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = Logger.Track(new()
            {
                [TrackingKeys.ShipmentNumber] = ShipmentNumberOf(result.Value),
                [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
            });
            LogSucceeded(result.Value);
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = Logger.Track(new()
            {
                [TrackingKeys.Outcome] = TrackingKeys.Refused,
                [TrackingKeys.FailureCode] = refusal.ErrorCode,
            });
            LogRefused(message, refusal.ErrorMessage);
        }

        return result;
    }

    protected abstract ILogger Logger { get; }

    protected abstract string CorrelationIdOf(TCommand command);

    protected abstract string ShipmentNumberOf(TResult result);

    protected abstract void LogSucceeded(TResult result);

    protected abstract void LogRefused(TCommand command, string reason);
}
