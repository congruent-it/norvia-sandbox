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

    /// <summary>A Carrier Persona sending its Carrier invoice through the Partner API.</summary>
    public const string SendCarrierInvoice = "Send Carrier invoice";

    /// <summary>Finance accepting a Carrier invoice line that waits for it, in the ERP.</summary>
    public const string AcceptCarrierInvoiceLine = "Accept Carrier invoice line";

    /// <summary>Finance rejecting a Carrier invoice line that waits for it, in the ERP.</summary>
    public const string RejectCarrierInvoiceLine = "Reject Carrier invoice line";

    /// <summary>A Carrier Persona's invoices page taking the outcome of a line.</summary>
    public const string ReceiveInvoiceOutcome = "Receive invoice outcome";
}
