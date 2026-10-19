using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.Issue;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// Finance's <b>Invoice now</b> for one Shipper (E1): an Invoice run over all its Waiting lines. It runs in a scope of its own, as the
/// circuit's lives as long as the page, and in a span that is the UI's hop on the invoice's trail. A request sent twice, or two runs at
/// once, never invoice a line twice: the one that loses a conflict runs again and finds nothing Waiting.
/// </summary>
internal sealed partial class InvoiceNowAction(IServiceScopeFactory scopes, ErpActivities activities, ILogger<InvoiceNowAction> logger)
{
    public async Task<InvoiceNowOutcome> RunAsync(string shipperId, CancellationToken cancellationToken = default)
    {
        using var span = activities.Source.StartActivity("Invoice now", ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.Invoicing, [TrackingKeys.PartyId] = shipperId });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var retrier = scope.ServiceProvider.GetRequiredService<IConflictRetrier>();
            var result = await retrier.RunAsync(mediator => mediator.Send(new IssueShipperInvoiceCommand(shipperId), cancellationToken).AsTask());

            if (result.Status == ResultStatus.NotFound)
            {
                return Refused(shipperId, string.Join(' ', result.Errors));
            }

            if (!result.IsSuccess)
            {
                return Refused(shipperId, string.Join(' ', result.Errors.Concat(result.ValidationErrors.Select(error => error.ErrorMessage))));
            }

            if (result.Value.Invoice is not { } invoice)
            {
                using var answered = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
                LogNothingWaiting(logger, shipperId);
                return new InvoiceNowOutcome.NothingWaiting();
            }

            // The invoice's own correlation ID: the UI rarely starts a trail itself, and here it continues the one the ERP created.
            using var issued = logger.Track(new() { [TrackingKeys.CorrelationId] = invoice.CorrelationId, [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogIssued(logger, invoice.InvoiceNumber, shipperId);
            return new InvoiceNowOutcome.Issued(invoice.InvoiceNumber, invoice.Lines.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            using var failed = logger.Track(new() { [TrackingKeys.Outcome] = "Failed" });
            LogFailed(logger, exception, shipperId);
            return new InvoiceNowOutcome.Failed("The ERP couldn't issue the invoice. Nothing was invoiced; try again.");
        }
    }

    private InvoiceNowOutcome.Failed Refused(string shipperId, string reason)
    {
        using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused });
        LogRefused(logger, shipperId, reason);
        return new InvoiceNowOutcome.Failed(reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Invoice now issued Shipper invoice {InvoiceNumber} to {ShipperId}")]
    private static partial void LogIssued(ILogger logger, string invoiceNumber, string shipperId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Invoice now for {ShipperId}: no Waiting lines, so no invoice")]
    private static partial void LogNothingWaiting(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invoice now for {ShipperId} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string shipperId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invoice now for {ShipperId} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string shipperId);
}
