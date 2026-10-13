using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Application.Reports.StartTrip;

/// <summary>
/// Traces a Driver's Start Trip on the trail of the Driver's action (coding guidelines §7): the <c>flow</c>, the <c>tripNumber</c>, the Driver as
/// <c>partyId</c> and, once the report exists, its <c>correlationId</c> and <c>sourceMessageId</c> (the Driver app event ID), then the <c>outcome</c>.
/// A Start Trip that is not found is Refused with <c>TripNotFound</c>.
/// </summary>
public sealed partial class TrackStartTripInLoggingScope(ILogger<TrackStartTripInLoggingScope> logger)
    : IPipelineBehavior<StartTripCommand, Result<StartTripResult>>
{
    public async ValueTask<Result<StartTripResult>> Handle(
        StartTripCommand message, MessageHandlerDelegate<StartTripCommand, Result<StartTripResult>> next, CancellationToken cancellationToken)
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
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = TrackingFailureCodes.TripNotFound });
            LogRefused(logger, message.TripNumber);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver started Trip {TripNumber} (already reported: {AlreadyReported})")]
    private static partial void LogRecorded(ILogger logger, string tripNumber, bool alreadyReported);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver's Start Trip for {TripNumber} refused: the Driver has no such Trip")]
    private static partial void LogRefused(ILogger logger, string tripNumber);
}
