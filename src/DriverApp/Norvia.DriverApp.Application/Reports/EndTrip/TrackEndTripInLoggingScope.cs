using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Application.Reports.EndTrip;

/// <summary>
/// Traces a Driver's End Trip on the trail of the Driver's action (coding guidelines §7): the <c>flow</c>, the <c>tripNumber</c>, the Driver as
/// <c>partyId</c> and, once the report exists, its <c>correlationId</c> and <c>sourceMessageId</c> (the Driver app event ID), then the <c>outcome</c>.
/// An End Trip the Driver app does not take is Refused with a failure code of the reason: <c>TripNotFound</c>, <c>TripNotUnderWay</c> or <c>StopsOpen</c>.
/// </summary>
public sealed partial class TrackEndTripInLoggingScope(ILogger<TrackEndTripInLoggingScope> logger)
    : IPipelineBehavior<EndTripCommand, Result<EndTripResult>>
{
    public async ValueTask<Result<EndTripResult>> Handle(
        EndTripCommand message, MessageHandlerDelegate<EndTripCommand, Result<EndTripResult>> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.Flow] = Flows.TripExecution,
            [TrackingKeys.TripNumber] = message.TripNumber,
            [TrackingKeys.PartyId] = message.DriverId,
        });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var recorded = logger.Track(new()
            {
                [TrackingKeys.SourceMessageId] = result.Value.ReportId.ToString(),
                [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
            });
            LogRecorded(logger, message.TripNumber, result.Value.AlreadyReported);
        }
        else
        {
            var code = result.Status == ResultStatus.NotFound ? TrackingFailureCodes.TripNotFound
                : result.Errors.Contains(EndTripHandler.NotStartedText) ? TrackingFailureCodes.TripNotUnderWay
                : TrackingFailureCodes.StopsOpen;
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = code });
            LogRefused(logger, message.TripNumber, code);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver ended Trip {TripNumber} (already reported: {AlreadyReported})")]
    private static partial void LogRecorded(ILogger logger, string tripNumber, bool alreadyReported);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver's End Trip for {TripNumber} was not taken: {FailureCode}")]
    private static partial void LogRefused(ILogger logger, string tripNumber, string failureCode);
}
