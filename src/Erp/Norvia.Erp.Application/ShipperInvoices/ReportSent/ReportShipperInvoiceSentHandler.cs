using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Application.ShipperInvoices.ReportSent;

public sealed class ReportShipperInvoiceSentHandler(IRepository<ShipperInvoice> invoices, TimeProvider time)
    : ICommandHandler<ReportShipperInvoiceSentCommand, Result<ShipperInvoiceReportResult>>
{
    public async ValueTask<Result<ShipperInvoiceReportResult>> Handle(ReportShipperInvoiceSentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.InvoiceNumber)
            || await invoices.FirstOrDefaultAsync(new ShipperInvoiceToReportSpec(new InvoiceNumber(command.InvoiceNumber)), cancellationToken) is not { } invoice)
        {
            return Result<ShipperInvoiceReportResult>.Invalid(
                new ValidationError(
                    "ShipperInvoice",
                    $"The ERP has no Shipper invoice {command.InvoiceNumber}.",
                    nameof(ShipperInvoiceRefusalCode.ShipperInvoiceNotFound),
                    ValidationSeverity.Error));
        }

        if (!invoice.ReportSent(time.GetUtcNow()))
        {
            // Reported already: ignored, changing nothing.
            return new ShipperInvoiceReportResult(invoice.Id.Value, invoice.Status.ToString(), invoice.SentAt, Ignored: true);
        }

        await invoices.SaveChangesAsync(cancellationToken);
        return new ShipperInvoiceReportResult(invoice.Id.Value, invoice.Status.ToString(), invoice.SentAt);
    }
}
