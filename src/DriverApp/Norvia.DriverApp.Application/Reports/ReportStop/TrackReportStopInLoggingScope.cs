using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.DriverApp.Application.Events;

namespace Norvia.DriverApp.Application.Reports.ReportStop;

/// <summary>
/// Traces a Driver's Stop report on the trail of the Driver's action (coding guidelines §7): the <c>flow</c>, the <c>tripNumber</c>, the Driver as
/// <c>partyId</c> and, once the report exists, its <c>sourceMessageId</c> (the Driver app event ID), then the <c>outcome</c>. A report the Driver app
/// does not take is Refused with a failure code of the reason.
/// </summary>
public sealed partial class TrackReportStopInLoggingScope(ILogger<TrackReportStopInLoggingScope> logger)
    : IPipelineBehavior<ReportStopCommand, Result<ReportStopResult>>
{
    public async ValueTask<Result<ReportStopResult>> Handle(
        ReportStopCommand message, MessageHandlerDelegate<ReportStopCommand, Result<ReportStopResult>> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.Flow] = Flows.TripExecution,
            [TrackingKeys.TripNumber] = message.TripNumber,
            [TrackingKeys.ShipmentNumber] = message.ShipmentNumber,
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
            LogRecorded(logger, message.Action, message.ShipmentNumber, message.TripNumber, result.Value.AlreadyReported);
        }
        else
        {
            var code = result.Status switch
            {
                ResultStatus.NotFound => TrackingFailureCodes.StopNotFound,
                ResultStatus.Invalid => TrackingFailureCodes.StopReportInvalid,
                _ => TrackingFailureCodes.StopNotAvailable,
            };
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = code });
            LogRefused(logger, message.Action, message.ShipmentNumber, message.TripNumber, code);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver reported {Action} for {ShipmentNumber} on Trip {TripNumber} (already reported: {AlreadyReported})")]
    private static partial void LogRecorded(ILogger logger, StopActionDto action, string shipmentNumber, string tripNumber, bool alreadyReported);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Driver's {Action} for {ShipmentNumber} on Trip {TripNumber} was not taken: {FailureCode}")]
    private static partial void LogRefused(ILogger logger, StopActionDto action, string shipmentNumber, string tripNumber, string failureCode);
}
