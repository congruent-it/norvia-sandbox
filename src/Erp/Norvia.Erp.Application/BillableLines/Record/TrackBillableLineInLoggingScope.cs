using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.BillableLines.Record;

/// <summary>
/// Traces the recording of a Billable line on the trail it continues, that of the transaction that made the Transport Order Billable
/// (coding guidelines §7): <c>correlationId</c> and <c>transportOrderNumber</c> for the whole call, and <c>outcome</c> for its outcome.
/// A repeat records Succeeded, as it is the original line again; a refusal records Refused with its <c>failureCode</c>. The API's request
/// scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackBillableLineInLoggingScope(ILogger<TrackBillableLineInLoggingScope> logger)
    : IPipelineBehavior<RecordBillableLineCommand, Result<RecordBillableLineResult>>
{
    public async ValueTask<Result<RecordBillableLineResult>> Handle(
        RecordBillableLineCommand message,
        MessageHandlerDelegate<RecordBillableLineCommand, Result<RecordBillableLineResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = message.Line.BillingCorrelationId,
            [TrackingKeys.TransportOrderNumber] = message.Line.TransportOrderNumber,
            [TrackingKeys.ShipmentNumber] = message.Line.ShipmentNumber,
        });

        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Repeat)
            {
                LogRepeat(logger, message.Line.TransportOrderNumber);
            }
            else
            {
                LogRecorded(logger, message.Line.TransportOrderNumber);
            }
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, message.Line.TransportOrderNumber, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Billable line for Transport Order {TransportOrderNumber} recorded as Waiting")]
    private static partial void LogRecorded(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Billable line for Transport Order {TransportOrderNumber} was already recorded; answering with the original")]
    private static partial void LogRepeat(ILogger logger, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Billable line for Transport Order {TransportOrderNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string transportOrderNumber, string reason);
}
