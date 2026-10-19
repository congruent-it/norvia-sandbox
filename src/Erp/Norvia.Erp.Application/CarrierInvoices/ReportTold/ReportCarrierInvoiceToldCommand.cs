using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.CarrierInvoices.ReportTold;

/// <summary>
/// The Carrier invoice outcome Logic App reports that the Carrier's invoice outcome endpoint acknowledged the outcome of an Approved
/// Carrier invoice (E2).
/// </summary>
/// <param name="CorrelationId">The correlation ID API Management created for the Carrier invoice, which the event and the Logic App's report carry.</param>
public sealed record ReportCarrierInvoiceToldCommand(string CarrierId, string InvoiceNumber, string CorrelationId)
    : ICommand<Result<CarrierInvoiceReportResult>>;

/// <param name="Ignored">True when the outcome was reported already, or the invoice isn't Approved: the report changed nothing.</param>
public sealed record CarrierInvoiceReportResult(string CarrierId, string InvoiceNumber, DateTimeOffset? ToldAt, bool Ignored = false);
