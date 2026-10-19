using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Application.CarrierInvoices.Receive;

/// <summary>
/// Traces the receiving of a Carrier invoice on the trail API Management started for it (coding guidelines §7): <c>correlationId</c>,
/// <c>documentNumber</c> (the Carrier's invoice number) and <c>partyId</c> (the Carrier) for the whole call, and <c>outcome</c> for its
/// outcome. A repeat records Succeeded, as it is the original Carrier invoice again; a refusal records Refused with its
/// <c>failureCode</c>. The API's request scope carries <c>flow</c>.
/// </summary>
public sealed partial class TrackCarrierInvoiceInLoggingScope(ILogger<TrackCarrierInvoiceInLoggingScope> logger)
    : IPipelineBehavior<ReceiveCarrierInvoiceCommand, Result<ReceiveCarrierInvoiceResult>>
{
    public async ValueTask<Result<ReceiveCarrierInvoiceResult>> Handle(
        ReceiveCarrierInvoiceCommand message,
        MessageHandlerDelegate<ReceiveCarrierInvoiceCommand, Result<ReceiveCarrierInvoiceResult>> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(next);

        var number = message.Invoice.InvoiceNumber;
        using var trail = logger.Track(new()
        {
            [TrackingKeys.CorrelationId] = message.Invoice.CorrelationId,
            [TrackingKeys.DocumentNumber] = number,
            [TrackingKeys.PartyId] = message.Invoice.CarrierId.Value,
        });

        var result = await next(message, cancellationToken);
        if (result.IsSuccess)
        {
            using var succeeded = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            if (result.Value.Repeat)
            {
                LogRepeat(logger, number);
            }
            else
            {
                LogReceived(logger, number, result.Value.Invoice.Status);
            }
        }
        else
        {
            var refusal = result.ValidationErrors.First();
            using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = refusal.ErrorCode });
            LogRefused(logger, number, refusal.ErrorMessage);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier invoice {InvoiceNumber} received as {Status}")]
    private static partial void LogReceived(ILogger logger, string invoiceNumber, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier invoice {InvoiceNumber} was already received; answering with the original")]
    private static partial void LogRepeat(ILogger logger, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier invoice {InvoiceNumber} refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string reason);
}
