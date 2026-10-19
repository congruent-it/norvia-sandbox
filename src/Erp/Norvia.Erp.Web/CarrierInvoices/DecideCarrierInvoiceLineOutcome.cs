namespace Norvia.Erp.Web.CarrierInvoices;

/// <summary>What deciding a Carrier invoice line did.</summary>
internal abstract record DecideCarrierInvoiceLineOutcome
{
    /// <summary>The line was decided. <paramref name="InvoiceApproved"/> is true when it was the last line, so the invoice is Approved.</summary>
    public sealed record Decided(bool InvoiceApproved) : DecideCarrierInvoiceLineOutcome;

    /// <summary>The line wasn't decided, and the reason says why.</summary>
    public sealed record Refused(string Reason) : DecideCarrierInvoiceLineOutcome;
}
