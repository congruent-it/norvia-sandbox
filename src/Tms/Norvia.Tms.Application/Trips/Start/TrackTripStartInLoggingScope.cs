using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Trips.Start;

/// <summary>
/// Traces a Driver's Start Trip (P12) on the trail of the Driver's action (coding guidelines §7): its <c>correlationId</c> and
/// <c>tripNumber</c> for the whole call, and <c>outcome</c> for its outcome, with the <c>failureCode</c> of a refusal, in the logging scope
/// and on the request's trace. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackTripStartInLoggingScope(ILogger<TrackTripStartInLoggingScope> logger)
    : IPipelineBehavior<StartTripCommand, Result<StartTripResult>>
{
    public async ValueTask<Result<StartTripResult>> Handle(
        StartTripCommand message, MessageHandlerDelegate<StartTripCommand, Result<StartTripResult>> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new() { [TrackingKeys.CorrelationId] = message.CorrelationId, [TrackingKeys.TripNumber] = message.Number });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var scope = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStarted(logger, result.Value.Number, result.Value.Version);
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripNumber} started at version {Version}")]
    private static partial void LogStarted(ILogger logger, string tripNumber, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Start Trip refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}
