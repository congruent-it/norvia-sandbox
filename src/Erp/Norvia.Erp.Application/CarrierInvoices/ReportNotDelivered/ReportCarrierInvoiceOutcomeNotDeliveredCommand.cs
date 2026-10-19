using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.CarrierInvoices.ReportTold;

namespace Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;

/// <summary>
/// The Carrier invoice outcome Logic App reports that the outcome of an Approved Carrier invoice never reached the Carrier, with its
/// technical cause (E2): the ERP flags the invoice Carrier not told and publishes <c>CarrierInvoiceOutcomeNotDelivered</c>. A report for
/// an invoice whose outcome was reported already is ignored.
/// </summary>
/// <param name="CorrelationId">The correlation ID API Management created for the Carrier invoice, which the event and the Logic App's report carry.</param>
/// <param name="StatusCode">The Carrier endpoint's HTTP status code, or null when the failure had none, such as a timeout.</param>
/// <param name="Message">The technical cause in plain language.</param>
public sealed record ReportCarrierInvoiceOutcomeNotDeliveredCommand(string CarrierId, string InvoiceNumber, string CorrelationId, int? StatusCode, string Message)
    : ICommand<Result<CarrierInvoiceReportResult>>;
