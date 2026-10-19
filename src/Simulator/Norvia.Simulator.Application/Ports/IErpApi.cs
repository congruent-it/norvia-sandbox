namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The ERP API, the door Finance invoices through (ADR 0001): <b>Invoice now</b> for a Shipper and a Credit note on a line of one of its invoices, the two
/// actions the ERP UI has. The ERP unreachable is an answer, not an error: <see cref="Domain.Log.LogOutcome.InvoicingNotReceived"/>.
/// </summary>
public interface IErpApi
{
    /// <summary>Runs the Invoice run for one Shipper over all its Waiting lines. The answer is <see cref="Domain.Log.LogOutcome.NothingToInvoice"/> when none is Waiting.</summary>
    Task<InvoiceNowAnswer> InvoiceNowAsync(string shipperId, CancellationToken cancellationToken);

    /// <summary>Issues a Credit note on one line of an issued invoice, for an amount without VAT.</summary>
    Task<CreditNoteAnswer> IssueCreditNoteAsync(string invoiceNumber, string transportOrderNumber, decimal amountWithoutVat, string reason, CancellationToken cancellationToken);
}
