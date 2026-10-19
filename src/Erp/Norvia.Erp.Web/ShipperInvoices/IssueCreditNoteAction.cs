using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes.Issue;
using Norvia.Erp.Application.IntegrationEvents;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// Finance's <b>Credit note</b> on one line of a Shipper invoice (E1): the ERP issues it and publishes <c>CreditNoteIssued</c>. It runs in a
/// scope and a span of its own, like Invoice now. The Credit note starts a trail of its own, which the answer names.
/// </summary>
internal sealed partial class IssueCreditNoteAction(IServiceScopeFactory scopes, ErpActivities activities, ILogger<IssueCreditNoteAction> logger)
{
    public async Task<IssueCreditNoteOutcome> RunAsync(
        string invoiceNumber,
        string transportOrderNumber,
        decimal amount,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        using var span = activities.Source.StartActivity("Issue Credit note", ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.Invoicing, [TrackingKeys.DocumentNumber] = invoiceNumber });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var retrier = scope.ServiceProvider.GetRequiredService<IConflictRetrier>();
            var result = await retrier.RunAsync(
                mediator => mediator.Send(new IssueCreditNoteCommand(invoiceNumber, transportOrderNumber, amount, reason), cancellationToken).AsTask());

            if (!result.IsSuccess)
            {
                using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused });
                var message = string.Join(' ', result.Errors.Concat(result.ValidationErrors.Select(error => error.ErrorMessage)));
                LogRefused(logger, invoiceNumber, transportOrderNumber, message);
                return new IssueCreditNoteOutcome.Refused(message);
            }

            using var issued = logger.Track(new()
            {
                [TrackingKeys.CorrelationId] = result.Value.CorrelationId,
                [TrackingKeys.DocumentNumber] = result.Value.CreditNoteNumber,
                [TrackingKeys.Outcome] = TrackingKeys.Succeeded,
            });
            LogIssued(logger, result.Value.CreditNoteNumber, invoiceNumber, transportOrderNumber);
            return new IssueCreditNoteOutcome.Issued(result.Value.CreditNoteNumber);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            using var failed = logger.Track(new() { [TrackingKeys.Outcome] = "Failed" });
            LogFailed(logger, exception, invoiceNumber, transportOrderNumber);
            return new IssueCreditNoteOutcome.Refused("The ERP couldn't issue the Credit note. Nothing was issued; try again.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued Credit note {CreditNoteNumber} on {InvoiceNumber} line {TransportOrderNumber}")]
    private static partial void LogIssued(ILogger logger, string creditNoteNumber, string invoiceNumber, string transportOrderNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credit note on {InvoiceNumber} line {TransportOrderNumber} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string transportOrderNumber, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Credit note on {InvoiceNumber} line {TransportOrderNumber} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string invoiceNumber, string transportOrderNumber);
}
