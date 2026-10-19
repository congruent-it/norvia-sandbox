using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Application.CarrierInvoices.ReportTold;

public sealed class ReportCarrierInvoiceToldHandler(IRepository<CarrierInvoice> invoices, TimeProvider time)
    : ICommandHandler<ReportCarrierInvoiceToldCommand, Result<CarrierInvoiceReportResult>>
{
    public async ValueTask<Result<CarrierInvoiceReportResult>> Handle(ReportCarrierInvoiceToldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.CarrierId)
            || string.IsNullOrWhiteSpace(command.InvoiceNumber)
            || await invoices.FirstOrDefaultAsync(new CarrierInvoiceToReportSpec(new CarrierId(command.CarrierId), command.InvoiceNumber), cancellationToken) is not { } invoice)
        {
            return CarrierInvoiceRefused.Because<CarrierInvoiceReportResult>(CarrierInvoiceRefusal.InvoiceNotFound(command.CarrierId, command.InvoiceNumber));
        }

        if (!invoice.ReportTold(time.GetUtcNow()))
        {
            // Reported already, or not Approved: ignored, changing nothing.
            return new CarrierInvoiceReportResult(invoice.CarrierId.Value, invoice.InvoiceNumber, invoice.ToldAt, Ignored: true);
        }

        await invoices.UpdateAsync(invoice, cancellationToken);
        return new CarrierInvoiceReportResult(invoice.CarrierId.Value, invoice.InvoiceNumber, invoice.ToldAt);
    }
}
