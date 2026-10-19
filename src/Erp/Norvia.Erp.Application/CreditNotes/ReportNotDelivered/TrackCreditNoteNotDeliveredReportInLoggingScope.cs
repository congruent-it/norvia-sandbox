using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.CreditNotes.ReportSent;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CreditNotes.ReportNotDelivered;

/// <summary>
/// Traces the Shipper invoice Logic App's Not delivered report on a Credit note on the Credit note's trail (coding guidelines §7), as the
/// Sent report is: <c>correlationId</c> and <c>documentNumber</c> for the whole call and <c>outcome</c> for its outcome. A report for a
/// Credit note that isn't Issued any more is ignored and logged as such; an unknown Credit note records Refused with its <c>failureCode</c>.
/// </summary>
public sealed partial class TrackCreditNoteNotDeliveredReportInLoggingScope(ILogger<TrackCreditNoteNotDeliveredReportInLoggingScope> logger)
    : IPipelineBehavior<ReportCreditNoteNotDeliveredCommand, Result<CreditNoteReportResult>>
{
    public async ValueTask<Result<CreditNoteReportResult>> Handle(
        ReportCreditNoteNotDeliveredCommand message,
        MessageHandlerDelegate<ReportCreditNoteNotDeliveredCommand, Result<CreditNoteReportResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.DocumentNumber] = message.CreditNoteNumber,
        });

        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Ignored)
            {
                LogIgnored(logger, message.CreditNoteNumber, result.Value.Status);
            }
            else
            {
                LogNotDelivered(logger, message.CreditNoteNumber, message.StatusCode, message.Message);
            }
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, message.CreditNoteNumber, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credit note {CreditNoteNumber} reported Not delivered (status {StatusCode}): {Cause}")]
    private static partial void LogNotDelivered(ILogger logger, string creditNoteNumber, int? statusCode, string cause);

    [LoggerMessage(Level = LogLevel.Information, Message = "Credit note {CreditNoteNumber} is {Status}, not Issued; the Not delivered report is ignored")]
    private static partial void LogIgnored(ILogger logger, string creditNoteNumber, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Not delivered report on Credit note {CreditNoteNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string creditNoteNumber, string reason);
}
