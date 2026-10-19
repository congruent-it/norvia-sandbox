namespace Norvia.Simulator.Domain.Invoicing;

/// <summary>What Finance's and the Shippers' invoicing steps are called: the log's action, and the span the Simulator sends the call in, which are the ERP UI's names for the same actions.</summary>
public static class InvoicingActions
{
    /// <summary>Who invoices: Finance, in the ERP.</summary>
    public const string FinanceActor = "Finance";

    public const string InvoiceNow = "Invoice now";

    public const string IssueCreditNote = "Issue Credit note";

    /// <summary>A Shipper Persona's inbox taking the invoice.</summary>
    public const string ReceiveInvoice = "Receive invoice";

    /// <summary>A Shipper Persona's inbox taking the Credit note.</summary>
    public const string ReceiveCreditNote = "Receive Credit note";
}
