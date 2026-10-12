using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.Trips.Complete;

/// <summary>
/// Traces a Driver's End Trip (P12) on the trail of the Driver's action (coding guidelines §7): its <c>correlationId</c> and
/// <c>tripNumber</c> for the whole call, and <c>outcome</c> for its outcome, with the <c>failureCode</c> of a refusal, in the logging scope
/// and on the request's trace. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackTripEndInLoggingScope(ILogger<TrackTripEndInLoggingScope> logger)
    : IPipelineBehavior<EndTripCommand, Result<EndTripResult>>
{
    public async ValueTask<Result<EndTripResult>> Handle(
        EndTripCommand message, MessageHandlerDelegate<EndTripCommand, Result<EndTripResult>> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new() { [TrackingKeys.CorrelationId] = message.CorrelationId, [TrackingKeys.TripNumber] = message.Number });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var scope = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogEnded(logger, result.Value.Number, result.Value.Version);
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripNumber} ended at version {Version}")]
    private static partial void LogEnded(ILogger logger, string tripNumber, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "The End Trip refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}
