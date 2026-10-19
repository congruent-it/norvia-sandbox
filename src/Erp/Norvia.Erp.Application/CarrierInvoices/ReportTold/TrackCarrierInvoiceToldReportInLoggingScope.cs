using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CarrierInvoices.ReportTold;

/// <summary>
/// Traces the Carrier invoice outcome Logic App's report on the trail of the Carrier invoice (coding guidelines §7): <c>correlationId</c>,
/// <c>partyId</c> and <c>documentNumber</c> for the whole call and <c>outcome</c> for its outcome. A report that changes nothing is ignored
/// and logged as such, still Succeeded; an unknown invoice records Refused with its <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackCarrierInvoiceToldReportInLoggingScope(ILogger<TrackCarrierInvoiceToldReportInLoggingScope> logger)
    : IPipelineBehavior<ReportCarrierInvoiceToldCommand, Result<CarrierInvoiceReportResult>>
{
    public async ValueTask<Result<CarrierInvoiceReportResult>> Handle(
        ReportCarrierInvoiceToldCommand message,
        MessageHandlerDelegate<ReportCarrierInvoiceToldCommand, Result<CarrierInvoiceReportResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.PartyId] = message.CarrierId,
            [TrackingKeys.DocumentNumber] = message.InvoiceNumber,
        });

        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Ignored)
            {
                LogIgnored(logger, message.InvoiceNumber, message.CarrierId);
            }
            else
            {
                LogTold(logger, message.InvoiceNumber, message.CarrierId);
            }
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, message.InvoiceNumber, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier invoice {InvoiceNumber} of Carrier {CarrierId} reported Carrier told")]
    private static partial void LogTold(ILogger logger, string invoiceNumber, string carrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The outcome of Carrier invoice {InvoiceNumber} of Carrier {CarrierId} was reported already, or the invoice isn't Approved; the report is ignored")]
    private static partial void LogIgnored(ILogger logger, string invoiceNumber, string carrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report on Carrier invoice {InvoiceNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string reason);
}
