using System.Diagnostics;
using Ardalis.Result;
using Norvia.Agents;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Web.ShipperInvoices;

namespace Norvia.Erp.Web.CarrierInvoices;

/// <summary>
/// Finance's decision on one Carrier invoice line (E2): accept it, accept it at an adjusted amount, or reject it. It runs in a scope and a
/// span of its own, like Credit note. The decision is traced on the Carrier invoice's own trail.
/// </summary>
internal sealed partial class DecideCarrierInvoiceLineAction(IServiceScopeFactory scopes, ErpActivities activities, ILogger<DecideCarrierInvoiceLineAction> logger)
{
    public Task<DecideCarrierInvoiceLineOutcome> AcceptAsync(
        string carrierId, string invoiceNumber, string carrierBookingReference, decimal? adjustedAmount, string? remark, AgentRun? actingOn = null, CancellationToken cancellationToken = default) =>
        RunAsync(
            "Accept Carrier invoice line",
            invoiceNumber,
            carrierBookingReference,
            new AcceptCarrierInvoiceLineCommand(new CarrierId(carrierId), invoiceNumber, carrierBookingReference, adjustedAmount, remark, actingOn),
            cancellationToken);

    public Task<DecideCarrierInvoiceLineOutcome> RejectAsync(
        string carrierId, string invoiceNumber, string carrierBookingReference, string? reason, AgentRun? actingOn = null, CancellationToken cancellationToken = default) =>
        RunAsync(
            "Reject Carrier invoice line",
            invoiceNumber,
            carrierBookingReference,
            new RejectCarrierInvoiceLineCommand(new CarrierId(carrierId), invoiceNumber, carrierBookingReference, reason, actingOn),
            cancellationToken);

    private async Task<DecideCarrierInvoiceLineOutcome> RunAsync(
        string spanName, string invoiceNumber, string carrierBookingReference, Mediator.ICommand<Result<CarrierInvoiceView>> command, CancellationToken cancellationToken)
    {
        using var span = activities.Source.StartActivity(spanName, ActivityKind.Internal);
        using var trail = logger.Track(new() { [TrackingKeys.Flow] = Flows.CarrierInvoices, [TrackingKeys.DocumentNumber] = invoiceNumber });
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var retrier = scope.ServiceProvider.GetRequiredService<IConflictRetrier>();
            var result = await retrier.RunAsync(mediator => mediator.Send(command, cancellationToken).AsTask());

            if (!result.IsSuccess)
            {
                using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused });
                var message = string.Join(' ', result.Errors.Concat(result.ValidationErrors.Select(error => error.ErrorMessage)));
                LogRefused(logger, carrierBookingReference, invoiceNumber, message);
                return new DecideCarrierInvoiceLineOutcome.Refused(message);
            }

            using var decided = logger.Track(new() { [TrackingKeys.CorrelationId] = result.Value.CorrelationId, [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogDecided(logger, carrierBookingReference, invoiceNumber, result.Value.Status);
            return new DecideCarrierInvoiceLineOutcome.Decided(result.Value.Status == "Approved");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            using var failed = logger.Track(new() { [TrackingKeys.Outcome] = "Failed" });
            LogFailed(logger, exception, carrierBookingReference, invoiceNumber);
            return new DecideCarrierInvoiceLineOutcome.Refused("The ERP couldn't record the decision. Nothing was decided; try again.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Decided {CarrierBookingReference} on Carrier invoice {InvoiceNumber}; the invoice is now {Status}")]
    private static partial void LogDecided(ILogger logger, string carrierBookingReference, string invoiceNumber, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deciding {CarrierBookingReference} on Carrier invoice {InvoiceNumber} was refused: {Reason}")]
    private static partial void LogRefused(ILogger logger, string carrierBookingReference, string invoiceNumber, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deciding {CarrierBookingReference} on Carrier invoice {InvoiceNumber} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string carrierBookingReference, string invoiceNumber);
}
