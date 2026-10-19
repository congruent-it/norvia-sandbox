using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;

namespace Norvia.Erp.Application.ShipperInvoices.ReportNotDelivered;

/// <summary>
/// The Shipper invoice Logic App reports that an invoice never reached the Shipper, with its technical cause (E1): the ERP records it
/// and publishes <c>ShipperInvoiceNotDelivered</c>. A report for an invoice that isn't Issued any more is ignored.
/// </summary>
/// <param name="CorrelationId">The correlation ID the ERP created for the invoice, which the event and the Logic App's report carry.</param>
/// <param name="StatusCode">The Shipper endpoint's HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ReportShipperInvoiceNotDeliveredCommand(string InvoiceNumber, string CorrelationId, int? StatusCode, string Message)
    : ICommand<Result<ShipperInvoiceReportResult>>;
