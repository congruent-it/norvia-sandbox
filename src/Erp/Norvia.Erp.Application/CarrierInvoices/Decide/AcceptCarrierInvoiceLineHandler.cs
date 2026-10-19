using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.CarrierInvoices;

namespace Norvia.Erp.Application.CarrierInvoices.Decide;

/// <summary>
/// Accepts a line in one save with the Carrier cost becoming Invoiced and, when it was the last line, the event. Two decisions on one
/// invoice, or on one Carrier cost, conflict on a row version: the one that loses is run again by the caller and finds what the winner left.
/// </summary>
public sealed partial class AcceptCarrierInvoiceLineHandler(
    IRepository<CarrierInvoice> invoices,
    IReadRepository<CarrierCost> costs,
    IOutbox outbox,
    TimeProvider time,
    ILogger<AcceptCarrierInvoiceLineHandler> logger)
    : ICommandHandler<AcceptCarrierInvoiceLineCommand, Result<CarrierInvoiceView>>
{
    public async ValueTask<Result<CarrierInvoiceView>> Handle(AcceptCarrierInvoiceLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoice = await invoices.FirstOrDefaultAsync(new CarrierInvoiceByNumberSpec(command.CarrierId, command.InvoiceNumber), cancellationToken);
        if (invoice is null)
        {
            return CarrierInvoiceRefused.Because<CarrierInvoiceView>(CarrierInvoiceRefusal.InvoiceNotFound(command.CarrierId.Value, command.InvoiceNumber));
        }

        using var trail = logger.TrackDecision(invoice);
        var number = invoice.Lines.FirstOrDefault(line => line.CarrierBookingReference == command.CarrierBookingReference)?.CarrierBookingNumber;
        var cost = number is null ? null : await costs.FirstOrDefaultAsync(new CarrierCostByBookingNumberSpec(number), cancellationToken);

        var at = time.GetUtcNow();
        var (approved, refusal) = invoice.Accept(command.CarrierBookingReference, command.AdjustedAmount, command.Remark, cost, at);
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
        LogAccepted(logger, command.InvoiceNumber, command.CarrierBookingReference, approved);
        return CarrierInvoiceViews.Of(invoice);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Accepting {CarrierBookingReference} on Carrier invoice {InvoiceNumber} refused: {Code}")]
    private static partial void LogRefused(ILogger logger, string invoiceNumber, string carrierBookingReference, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Accepted {CarrierBookingReference} on Carrier invoice {InvoiceNumber}; invoice approved: {Approved}")]
    private static partial void LogAccepted(ILogger logger, string invoiceNumber, string carrierBookingReference, bool approved);
}
