using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CreditNotes.ReportSent;

/// <summary>
/// Traces the Shipper invoice Logic App's report on a Credit note on the Credit note's trail (coding guidelines §7), as the report on an
/// invoice is: <c>correlationId</c> and <c>documentNumber</c> for the whole call and <c>outcome</c> for its outcome. A report for a Credit
/// note reported already is ignored and logged as such; an unknown Credit note records Refused with its <c>failureCode</c>.
/// </summary>
public sealed partial class TrackCreditNoteReportInLoggingScope(ILogger<TrackCreditNoteReportInLoggingScope> logger)
    : IPipelineBehavior<ReportCreditNoteSentCommand, Result<CreditNoteReportResult>>
{
    public async ValueTask<Result<CreditNoteReportResult>> Handle(
        ReportCreditNoteSentCommand message,
        MessageHandlerDelegate<ReportCreditNoteSentCommand, Result<CreditNoteReportResult>> next,
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
                LogIgnored(logger, message.CreditNoteNumber);
            }
            else
            {
                LogSent(logger, message.CreditNoteNumber);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Credit note {CreditNoteNumber} reported Sent")]
    private static partial void LogSent(ILogger logger, string creditNoteNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Credit note {CreditNoteNumber} was reported already; the report is ignored")]
    private static partial void LogIgnored(ILogger logger, string creditNoteNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report on Credit note {CreditNoteNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string creditNoteNumber, string reason);
}
