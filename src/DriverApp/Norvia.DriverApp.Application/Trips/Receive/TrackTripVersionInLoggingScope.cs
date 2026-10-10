using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Norvia.DriverApp.Application.Trips.Receive;

/// <summary>
/// Traces a Trip version on the trail the Dispatch started (coding guidelines §7): its <c>correlationId</c> and
/// <c>tripNumber</c> for the whole call, and <c>outcome</c> for its outcome. A version the Driver app ignores because it
/// holds an equal or later one records Succeeded: it isn't a refusal, only a no-op. A malformed version records Refused,
/// with its <c>failureCode</c>, and one refused while the Driver app is unavailable records Failed.
/// </summary>
public sealed partial class TrackTripVersionInLoggingScope(ILogger<TrackTripVersionInLoggingScope> logger)
    : IPipelineBehavior<ReceiveTripVersionCommand, Result<TripVersionOutcome>>
{
    private const string Flow = "trip-dispatch";

    public async ValueTask<Result<TripVersionOutcome>> Handle(
        ReceiveTripVersionCommand message,
        MessageHandlerDelegate<ReceiveTripVersionCommand, Result<TripVersionOutcome>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.Flow] = Flow,
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.TripNumber] = message.TripNumber,
        });
        var result = await next(message, cancellationToken);
        switch (result.Status)
        {
            case ResultStatus.Ok:
                using (logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded }))
                {
                    LogReceived(logger, message.TripNumber, message.Version, result.Value);
                }

                break;
            case ResultStatus.Invalid:
                using (logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = "InvalidTripVersion" }))
                {
                    LogRefused(logger, message.TripNumber, message.Version);
                }

                break;
            default:
                using (logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Failed, [TrackingKeys.FailureCode] = "DriverAppUnavailable" }))
                {
                    LogUnavailable(logger, message.TripNumber, message.Version);
                }

                break;
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripNumber} version {Version} received: {Outcome}")]
    private static partial void LogReceived(ILogger logger, string tripNumber, int version, TripVersionOutcome outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripNumber} version {Version} refused as malformed")]
    private static partial void LogRefused(ILogger logger, string tripNumber, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trip {TripNumber} version {Version} refused: the Driver app is unavailable")]
    private static partial void LogUnavailable(ILogger logger, string tripNumber, int version);
}
