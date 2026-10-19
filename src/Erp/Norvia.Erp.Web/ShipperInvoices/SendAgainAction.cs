using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.SendAgain;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// Finance's <b>Send again</b> for a Not delivered Shipper invoice (E1): the same invoice is Issued once more and
/// <c>ShipperInvoiceIssued</c> is published again. It runs in a scope and a span of its own, like Invoice now. A request sent twice
/// sends once: the second finds the invoice Issued and is refused.
/// </summary>
internal sealed partial class SendAgainAction(IServiceScopeFactory scopes, ErpActivities activities, ILogger<SendAgainAction> logger)
{
    public async Task<SendAgainOutcome> RunAsync(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        using var span = activities.Source.StartActivity("Send again", ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.Invoicing, [TrackingKeys.DocumentNumber] = invoiceNumber });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var retrier = scope.ServiceProvider.GetRequiredService<IConflictRetrier>();
            var result = await retrier.RunAsync(mediator => mediator.Send(new SendShipperInvoiceAgainCommand(invoiceNumber), cancellationToken).AsTask());

            if (!result.IsSuccess)
            {
                using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused });
                var reason = string.Join(' ', result.Errors.Concat(result.ValidationErrors.Select(error => error.ErrorMessage)));
                LogRefused(logger, invoiceNumber, reason);
                return new SendAgainOutcome.Refused(reason);
            }

            using var sent = logger.Track(new() { [TrackingKeys.CorrelationId] = result.Value.CorrelationId, [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogSentAgain(logger, invoiceNumber);
            return new SendAgainOutcome.SentAgain(invoiceNumber);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            using var failed = logger.Track(new() { [TrackingKeys.Outcome] = "Failed" });
            LogFailed(logger, exception, invoiceNumber);
            return new SendAgainOutcome.Refused("The ERP couldn't send the invoice again. Nothing was sent; try again.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Send again published Shipper invoice {InvoiceNumber} once more")]
    private static partial void LogSentAgain(ILogger logger, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Send again for {InvoiceNumber} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Send again for {InvoiceNumber} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string invoiceNumber);
}
