using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Exceptions;

namespace Norvia.Tms.Application.Exceptions.Resolve;

/// <summary>
/// Traces a Planner's resolution of an Exception on its own Message trail (coding guidelines §7): the
/// <c>correlationId</c> the TMS created for it for the whole call, and for its outcome <c>shipmentNumber</c> and
/// <c>outcome</c>, in the logging scope and on the request's trace. A resolution the TMS refuses records Refused with its
/// <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackExceptionResolutionInLoggingScope(ILogger<TrackExceptionResolutionInLoggingScope> logger)
    : IPipelineBehavior<ResolveExceptionCommand, Result<ResolveExceptionResult>>
{
    public async ValueTask<Result<ResolveExceptionResult>> Handle(
        ResolveExceptionCommand message,
        MessageHandlerDelegate<ResolveExceptionCommand, Result<ResolveExceptionResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = Track(new() { [TrackingKeys.CorrelationId] = message.CorrelationId });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = Track(new()
            {
                [TrackingKeys.ShipmentNumber] = result.Value.ShipmentNumber,
                [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
            });
            LogResolved(logger, result.Value.ExceptionNumber, result.Value.ShipmentNumber, result.Value.Outcome);
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = Track(new()
            {
                [TrackingKeys.Outcome] = TrackingKeys.Refused,
                [TrackingKeys.FailureCode] = refusal.ErrorCode,
            });
            LogRefused(logger, message.ExceptionNumber, refusal.ErrorMessage);
        }

        return result;
    }

    private IDisposable? Track(Dictionary<string, object?> keys)
    {
        foreach (var (key, value) in keys)
        {
            Activity.Current?.SetTag(key, value);
        }

        return logger.BeginScope(keys);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Exception {ExceptionNumber} of Shipment {ShipmentNumber} resolved as {Outcome}")]
    private static partial void LogResolved(ILogger logger, string exceptionNumber, string shipmentNumber, ExceptionOutcome outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolution of Exception {ExceptionNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string exceptionNumber, string reason);
}
