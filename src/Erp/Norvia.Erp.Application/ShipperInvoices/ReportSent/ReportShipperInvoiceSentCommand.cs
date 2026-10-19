using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.ShipperInvoices.ReportSent;

/// <summary>
/// The Shipper invoice Logic App reports that the Shipper's invoice endpoint acknowledged an invoice.
/// </summary>
/// <param name="CorrelationId">The correlation ID the ERP created for the invoice, which the event and the Logic App's report carry.</param>
public sealed record ReportShipperInvoiceSentCommand(string InvoiceNumber, string CorrelationId) : ICommand<Result<ShipperInvoiceReportResult>>;

/// <param name="Ignored">True when the invoice was reported already: the report changed nothing.</param>
public sealed record ShipperInvoiceReportResult(string InvoiceNumber, string Status, DateTimeOffset? SentAt, bool Ignored = false);
