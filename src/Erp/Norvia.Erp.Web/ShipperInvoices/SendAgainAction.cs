using System.Diagnostics;
using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes.SendAgain;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.SendAgain;

namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>
/// Finance's <b>Send again</b> for a Not delivered Shipper invoice or Credit note (E1): the same document is Issued once more and
/// <c>ShipperInvoiceIssued</c> or <c>CreditNoteIssued</c> is published again. It runs in a scope and a span of its own, like Invoice now. A request sent twice
/// sends once: the second finds the invoice Issued and is refused.
/// </summary>
internal sealed partial class SendAgainAction(IServiceScopeFactory scopes, ErpActivities activities, ILogger<SendAgainAction> logger)
{
    private const string CreditNotePrefix = "CN-";

    public async Task<SendAgainOutcome> RunAsync(string documentNumber, CancellationToken cancellationToken = default)
    {
        using var span = activities.Source.StartActivity("Send again", ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.Invoicing, [TrackingKeys.DocumentNumber] = documentNumber });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await SendAsync(scope.ServiceProvider.GetRequiredService<IConflictRetrier>(), documentNumber, cancellationToken);

            if (!result.IsSuccess)
            {
                using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused });
                var reason = string.Join(' ', result.Errors.Concat(result.ValidationErrors.Select(error => error.ErrorMessage)));
                LogRefused(logger, documentNumber, reason);
                return new SendAgainOutcome.Refused(reason);
            }

            using var sent = logger.Track(new() { [TrackingKeys.CorrelationId] = result.Value, [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogSentAgain(logger, documentNumber);
            return new SendAgainOutcome.SentAgain(documentNumber);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            using var failed = logger.Track(new() { [TrackingKeys.Outcome] = "Failed" });
            LogFailed(logger, exception, documentNumber);
            return new SendAgainOutcome.Refused("The ERP couldn't send the document again. Nothing was sent; try again.");
        }
    }

    /// <returns>The sent document's correlation ID, or why it wasn't sent again.</returns>
    private static async Task<Result<string>> SendAsync(IConflictRetrier retrier, string documentNumber, CancellationToken cancellationToken)
    {
        if (documentNumber.StartsWith(CreditNotePrefix, StringComparison.Ordinal))
        {
            var creditNote = await retrier.RunAsync(mediator => mediator.Send(new SendCreditNoteAgainCommand(documentNumber), cancellationToken).AsTask());
            return creditNote.Map(note => note.CorrelationId);
        }

        var invoice = await retrier.RunAsync(mediator => mediator.Send(new SendShipperInvoiceAgainCommand(documentNumber), cancellationToken).AsTask());
        return invoice.Map(view => view.CorrelationId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Send again published {DocumentNumber} once more")]
    private static partial void LogSentAgain(ILogger logger, string documentNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Send again for {DocumentNumber} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string documentNumber, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Send again for {DocumentNumber} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string documentNumber);
}
