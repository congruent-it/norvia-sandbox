using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;

/// <summary>
/// Traces the Shipper invoice Logic App's Not delivered report on the trail of the invoice (coding guidelines §7), as the Sent report is:
/// <c>correlationId</c> and <c>documentNumber</c> for the whole call and <c>outcome</c> for its outcome. A report for an invoice that isn't
/// Issued any more is ignored and logged as such; an unknown invoice records Refused with its <c>failureCode</c>.
/// </summary>
public sealed partial class TrackShipperInvoiceNotDeliveredReportInLoggingScope(ILogger<TrackShipperInvoiceNotDeliveredReportInLoggingScope> logger)
    : IPipelineBehavior<ReportShipperInvoiceNotDeliveredCommand, Result<ShipperInvoiceReportResult>>
{
    public async ValueTask<Result<ShipperInvoiceReportResult>> Handle(
        ReportShipperInvoiceNotDeliveredCommand message,
        MessageHandlerDelegate<ReportShipperInvoiceNotDeliveredCommand, Result<ShipperInvoiceReportResult>> next,
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
                LogIgnored(logger, message.InvoiceNumber, result.Value.Status);
            }
            else
            {
                LogNotDelivered(logger, message.InvoiceNumber, message.StatusCode, message.Message);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shipper invoice {InvoiceNumber} reported Not delivered (status {StatusCode}): {Cause}")]
    private static partial void LogNotDelivered(ILogger logger, string invoiceNumber, int? statusCode, string cause);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shipper invoice {InvoiceNumber} is {Status}, not Issued; the Not delivered report is ignored")]
    private static partial void LogIgnored(ILogger logger, string invoiceNumber, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Not delivered report on Shipper invoice {InvoiceNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string reason);
}
