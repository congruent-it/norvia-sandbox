using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Application.IntegrationEvents;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;
using Norvia.Erp.Domain.ShipperInvoices;

namespace Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;

public sealed class ReportShipperInvoiceNotDeliveredHandler(IRepository<ShipperInvoice> invoices, IOutbox outbox, TimeProvider time)
    : ICommandHandler<ReportShipperInvoiceNotDeliveredCommand, Result<ShipperInvoiceReportResult>>
{
    public async ValueTask<Result<ShipperInvoiceReportResult>> Handle(ReportShipperInvoiceNotDeliveredCommand command, CancellationToken cancellationToken)
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

        var at = time.GetUtcNow();
        if (!invoice.ReportNotDelivered(command.StatusCode, command.Message, at))
        {
            // Late or repeated: ignored, changing and publishing nothing.
            return new ShipperInvoiceReportResult(invoice.Id.Value, invoice.Status.ToString(), invoice.SentAt, Ignored: true);
        }

        // The status and the event are saved together, or neither is.
        outbox.Add(new ShipperInvoiceNotDeliveredV1(
            Guid.CreateVersion7(),
            at,
            invoice.CorrelationId,
            ShipperInvoiceNotDeliveredV1.InvoiceDocumentType,
            invoice.Id.Value,
            invoice.ShipperId.Value,
            invoice.NotDeliveredStatusCode,
            invoice.NotDeliveredMessage!));
        await invoices.SaveChangesAsync(cancellationToken);
        return new ShipperInvoiceReportResult(invoice.Id.Value, invoice.Status.ToString(), invoice.SentAt);
    }
}
