using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.CarrierInvoices.ReportTold;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;

public sealed class ReportCarrierInvoiceOutcomeNotDeliveredHandler(IRepository<CarrierInvoice> invoices, IOutbox outbox, TimeProvider time)
    : ICommandHandler<ReportCarrierInvoiceOutcomeNotDeliveredCommand, Result<CarrierInvoiceReportResult>>
{
    public async ValueTask<Result<CarrierInvoiceReportResult>> Handle(ReportCarrierInvoiceOutcomeNotDeliveredCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CarrierId)
            || string.IsNullOrWhiteSpace(command.InvoiceNumber)
            || await invoices.FirstOrDefaultAsync(new CarrierInvoiceToReportSpec(new CarrierId(command.CarrierId), command.InvoiceNumber), cancellationToken) is not { } invoice)
        {
            return CarrierInvoiceRefused.Because<CarrierInvoiceReportResult>(CarrierInvoiceRefusal.InvoiceNotFound(command.CarrierId, command.InvoiceNumber));
        }

        var at = time.GetUtcNow();
        if (!invoice.ReportNotDelivered(command.StatusCode, command.Message, at))
        {
            // Late or repeated: ignored, changing and publishing nothing.
            return new CarrierInvoiceReportResult(invoice.CarrierId.Value, invoice.InvoiceNumber, invoice.ToldAt, Ignored: true);
        }

        // The flag and the event are saved together, or neither is.
        outbox.Add(new CarrierInvoiceOutcomeNotDeliveredV1(
            Guid.CreateVersion7(),
            at,
            invoice.CorrelationId,
            invoice.CarrierId.Value,
            invoice.InvoiceNumber,
            invoice.NotToldStatusCode,
            invoice.NotToldMessage!));
        await invoices.UpdateAsync(invoice, cancellationToken);
        return new CarrierInvoiceReportResult(invoice.CarrierId.Value, invoice.InvoiceNumber, invoice.ToldAt);
    }
}
