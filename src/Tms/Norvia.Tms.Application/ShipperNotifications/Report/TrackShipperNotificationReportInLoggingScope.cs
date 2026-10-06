using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.ShipperNotifications;

namespace Norvia.Tms.Application.ShipperNotifications.Report;

/// <summary>
/// Traces a Shipper notification's sent or not-delivered report (P7) on the trail it continues, the Planner's action's (coding
/// guidelines §7): its <c>correlationId</c> for the whole call, and <c>outcome</c> for its outcome, in the logging scope and on the
/// request's trace. A report the TMS ignores, because the notification is no longer Requested, still records Succeeded: it isn't
/// a refusal, only a no-op. One the TMS refuses (an unknown notification ID) records Refused, with its <c>failureCode</c>. The API's
/// request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackShipperNotificationReportInLoggingScope<TCommand>(ILogger<TrackShipperNotificationReportInLoggingScope<TCommand>> logger)
    : IPipelineBehavior<TCommand, Result<ShipperNotificationReportResult>>
    where TCommand : IMessage, IShipperNotificationReportCommand
{
    public async ValueTask<Result<ShipperNotificationReportResult>> Handle(
        TCommand message,
        MessageHandlerDelegate<TCommand, Result<ShipperNotificationReportResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new() { [TrackingKeys.CorrelationId] = message.CorrelationId });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            LogOutcome(result.Value);
        }
        else
        {
            LogRefusal(result);
        }

        return result;
    }

    private void LogOutcome(ShipperNotificationReportResult outcome)
    {
        using var scope = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        if (outcome.Ignored)
        {
            LogIgnored(logger, outcome.NotificationId, outcome.Status);
        }
        else
        {
            LogRecorded(logger, outcome.NotificationId, outcome.Status);
        }
    }

    private void LogRefusal(Result<ShipperNotificationReportResult> result)
    {
        var refusal = result.ValidationErrors.First();
        using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
        LogRefused(logger, refusal.ErrorMessage);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper notification {NotificationId} report recorded: {Status}")]
    private static partial void LogRecorded(ILogger logger, string notificationId, ShipperNotificationStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper notification {NotificationId} report ignored, as it is {Status} and the report no longer applies")]
    private static partial void LogIgnored(ILogger logger, string notificationId, ShipperNotificationStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Shipper notification report refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string reason);
}
