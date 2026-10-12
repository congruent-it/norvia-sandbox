using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Application.Reports.ReportOutcome;

/// <summary>
/// Traces the Logic App's outcome report on the trail of the Driver's action (coding guidelines §7), under the correlation ID the report names:
/// <c>flow</c>, <c>correlationId</c>, <c>tripNumber</c> and <c>sourceMessageId</c> for the whole call, and <c>outcome</c> for its outcome. A report the Driver
/// app doesn't know is Refused with <c>ReportNotFound</c>, and one it can't read is Refused with <c>InvalidOutcome</c>.
/// </summary>
public sealed partial class TrackReportOutcomeInLoggingScope(ILogger<TrackReportOutcomeInLoggingScope> logger)
    : IPipelineBehavior<ReportOutcomeCommand, Result<ReportOutcomeResult>>
{
    public async ValueTask<Result<ReportOutcomeResult>> Handle(
        ReportOutcomeCommand message, MessageHandlerDelegate<ReportOutcomeCommand, Result<ReportOutcomeResult>> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.Flow] = Flows.TripExecution,
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.TripNumber] = message.TripNumber,
            [TrackingKeys.SourceMessageId] = message.DriverAppEventId.ToString(),
        });
        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var applied = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogTold(logger, message.Outcome, result.Value);
        }
        else
        {
            var code = result.Status == ResultStatus.NotFound ? "ReportNotFound" : "InvalidOutcome";
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = code });
            LogRefused(logger, code);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The report is {Outcome}: {Result}")]
    private static partial void LogTold(ILogger logger, ReportOutcomeKind outcome, ReportOutcomeResult result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The outcome report was refused: {Code}")]
    private static partial void LogRefused(ILogger logger, string code);
}
