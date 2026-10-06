using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;

namespace Norvia.Tms.Application.TransportOrders.Rate;

/// <summary>
/// Traces a re-rate on its Transport Order's Message trail (coding guidelines §7): the intake's <c>correlationId</c>, as
/// rating continues the intake's trail, with <c>partyId</c>, <c>transportOrderNumber</c>, <c>outcome</c> and, when
/// Rating fails again, <c>failureCode</c>, in the logging scope and on the request's trace. A re-rate that gives its
/// answer records Succeeded, even when Rating failed again. One the TMS refuses records Refused: on the intake's trail
/// when the order isn't Rating failed, on none when it doesn't exist. The API's request scope carries <c>flow</c>.
/// The rating worker traces the first rating itself, so a first rating passes straight through.
/// </summary>
public sealed partial class TrackReRateInLoggingScope(ILogger<TrackReRateInLoggingScope> logger)
    : IPipelineBehavior<RateTransportOrderCommand, Result<RateTransportOrderResult>>
{
    public async ValueTask<Result<RateTransportOrderResult>> Handle(
        RateTransportOrderCommand message,
        MessageHandlerDelegate<RateTransportOrderCommand, Result<RateTransportOrderResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);
        if (message.ReRateKey is null)
        {
            return await next(message, cancellationToken);
        }

        using var numbered = logger.Track(new() { [TrackingKeys.TransportOrderNumber] = message.TransportOrderNumber });
        var result = await next(message, cancellationToken);
        if (!result.IsSuccess)
        {
            var keys = new Dictionary<string, object?> { [TrackingKeys.Outcome] = TrackingKeys.Refused };
            if (!string.IsNullOrWhiteSpace(result.CorrelationId))
            {
                keys[TrackingKeys.CorrelationId] = result.CorrelationId;
            }

            using var refused = logger.Track(keys);
            LogRefused(logger, result.Errors);
        }
        else if (result.IsSuccess)
        {
            LogOutcome(message.TransportOrderNumber, result.Value);
        }

        return result;
    }

    private void LogOutcome(string transportOrderNumber, RateTransportOrderResult outcome)
    {
        var trail = new Dictionary<string, object?>
        {
            [TrackingKeys.CorrelationId] = outcome.CorrelationId,
            [TrackingKeys.PartyId] = outcome.ShipperId,
            [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
        };
        if (outcome.RatingFailure is { } failure)
        {
            trail[TrackingKeys.FailureCode] = failure.Code;
        }

        using var scope = logger.Track(trail);
        if (outcome.Repeat)
        {
            LogRepeat(logger, transportOrderNumber, outcome.RatingResult);
        }
        else if (outcome.RatingFailure is { } failedAgain)
        {
            LogFailedAgain(logger, transportOrderNumber, failedAgain.Code, failedAgain.Message);
        }
        else
        {
            LogReRated(logger, transportOrderNumber);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} re-rated: Rated")]
    private static partial void LogReRated(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transport Order {TransportOrderNumber} re-rated: Rating failed again with {RatingFailureCode}: {RatingFailureMessage}")]
    private static partial void LogFailedAgain(ILogger logger, string transportOrderNumber, string ratingFailureCode, string ratingFailureMessage);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport Order {TransportOrderNumber} re-rate answered as a repeat: {RatingResult}")]
    private static partial void LogRepeat(ILogger logger, string transportOrderNumber, Domain.TransportOrders.RatingResult ratingResult);

    [LoggerMessage(Level = LogLevel.Information, Message = "Re-rate refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, IEnumerable<string> reason);
}
