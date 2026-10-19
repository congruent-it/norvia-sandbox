using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.ShipperInvoices.ReportSent;

/// <summary>
/// Traces the Shipper invoice Logic App's report on the trail of the invoice (coding guidelines §7): <c>correlationId</c> and <c>documentNumber</c> for the whole
/// call and <c>outcome</c> for its outcome. A report for an invoice reported already is ignored and logged as such, still Succeeded, as
/// the invoice is Sent; an unknown invoice records Refused with its <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackShipperInvoiceReportInLoggingScope(ILogger<TrackShipperInvoiceReportInLoggingScope> logger)
    : IPipelineBehavior<ReportShipperInvoiceSentCommand, Result<ShipperInvoiceReportResult>>
{
    public async ValueTask<Result<ShipperInvoiceReportResult>> Handle(
        ReportShipperInvoiceSentCommand message,
        MessageHandlerDelegate<ReportShipperInvoiceSentCommand, Result<ShipperInvoiceReportResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = message.CorrelationId,
            [TrackingKeys.DocumentNumber] = message.InvoiceNumber,
        });

        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Ignored)
            {
                LogIgnored(logger, message.InvoiceNumber);
            }
            else
            {
                LogSent(logger, message.InvoiceNumber);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper invoice {InvoiceNumber} reported Sent")]
    private static partial void LogSent(ILogger logger, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper invoice {InvoiceNumber} was reported already; the report is ignored")]
    private static partial void LogIgnored(ILogger logger, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report on Shipper invoice {InvoiceNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string reason);
}
