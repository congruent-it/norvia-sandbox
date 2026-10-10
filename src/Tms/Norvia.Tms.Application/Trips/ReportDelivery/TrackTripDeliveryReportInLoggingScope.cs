using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Application.Trips.ReportDelivery;

/// <summary>
/// Traces a Trip dispatch report (P11) on the trail it continues, the Dispatch's or the send-again's (coding guidelines §7): its
/// <c>correlationId</c> and <c>tripNumber</c> for the whole call, and <c>outcome</c> for its outcome, in the logging scope and on the
/// request's trace. A report the TMS ignores, because the version is no longer Sending, still records Succeeded: it isn't a refusal,
/// only a no-op. One the TMS refuses (an unknown Trip or version) records Refused, with its <c>failureCode</c>. The API's request scope
/// carries <c>flow</c>.
/// </summary>
public sealed partial class TrackTripDeliveryReportInLoggingScope<TCommand>(ILogger<TrackTripDeliveryReportInLoggingScope<TCommand>> logger)
    : IPipelineBehavior<TCommand, Result<TripDeliveryReportResult>>
    where TCommand : IMessage, ITripDeliveryReportCommand
{
    public async ValueTask<Result<TripDeliveryReportResult>> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, Result<TripDeliveryReportResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new() { [TrackingKeys.CorrelationId] = message.CorrelationId, [TrackingKeys.TripNumber] = message.Number });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var scope = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Ignored)
            {
                LogIgnored(logger, result.Value.Number, result.Value.Version, result.Value.State);
            }
            else
            {
                LogRecorded(logger, result.Value.Number, result.Value.Version, result.Value.State);
            }
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripNumber} version {Version} delivery report recorded: {State}")]
    private static partial void LogRecorded(ILogger logger, string tripNumber, int version, TripDeliveryState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trip {TripNumber} version {Version} delivery report ignored, as the version is {State} and the report no longer applies")]
    private static partial void LogIgnored(ILogger logger, string tripNumber, int version, TripDeliveryState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Trip delivery report refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}
