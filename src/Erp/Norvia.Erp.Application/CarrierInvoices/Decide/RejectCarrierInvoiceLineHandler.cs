using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

/// <summary>Rejects a line in one save with, when it was the last line, the event. A conflicting decision is run again by the caller.</summary>
public sealed partial class RejectCarrierInvoiceLineHandler(
    IRepository<CarrierInvoice> invoices,
    IOutbox outbox,
    TimeProvider time,
    ILogger<RejectCarrierInvoiceLineHandler> logger)
    : ICommandHandler<RejectCarrierInvoiceLineCommand, Result<CarrierInvoiceView>>
{
    public async ValueTask<Result<CarrierInvoiceView>> Handle(RejectCarrierInvoiceLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(command.CarrierId, command.InvoiceNumber), cancellationToken);
        if (invoice is null)
        {
            return CarrierInvoiceRefused.Because<CarrierInvoiceView>(CarrierInvoiceRefusal.InvoiceNotFound(command.CarrierId.Value, command.InvoiceNumber));
        }

        using var trail = logger.TrackDecision(invoice);
        var at = time.GetUtcNow();
        var (approved, refusal) = invoice.Reject(command.CarrierBookingReference, command.Reason, at);
        if (refusal is not null)
        {
            LogRefused(logger, command.InvoiceNumber, command.CarrierBookingReference, refusal.Code.ToString());
            return CarrierInvoiceRefused.Because<CarrierInvoiceView>(refusal);
        }

        if (approved)
        {
            outbox.Add(CarrierInvoiceApprovedV1.Of(invoice, Guid.CreateVersion7(), at));
        }

        await invoices.UpdateAsync(invoice, cancellationToken);
        LogRejected(logger, command.InvoiceNumber, command.CarrierBookingReference, approved);
        return CarrierInvoiceViews.Of(invoice);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejecting {CarrierBookingReference} on Carrier invoice {InvoiceNumber} refused: {Code}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string carrierBookingReference, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected {CarrierBookingReference} on Carrier invoice {InvoiceNumber}; invoice approved: {Approved}")]
    private static partial void LogRejected(ILogger logger, string invoiceNumber, string carrierBookingReference, bool approved);
}
