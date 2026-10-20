namespace Norvia.Simulator.Domain.Invoicing;

/// <summary>
/// What Finance does once the deliveries are in, one percentage each: the share of the run's Shippers that Finance runs <b>Invoice now</b> for, and the share
/// of those invoices on which Finance issues a partial Credit note, on one line and with a reason. A Shipper whose Transport Orders have not become Billable
/// yet gets no invoice, which the log says. For the Carriers' side (E2): the share of Carrier invoices that carry one edited amount, a Price mismatch on
/// purpose, and the share of the lines left To decide that Finance rejects instead of accepting at the agreed amount.
/// </summary>
public sealed record InvoicingShares(
    int InvoicePercent = InvoicingShares.DefaultInvoicePercent,
    int CreditNotePercent = InvoicingShares.DefaultCreditNotePercent,
    int EditedCarrierInvoicePercent = InvoicingShares.DefaultEditedCarrierInvoicePercent,
    int RejectedCarrierLinePercent = InvoicingShares.DefaultRejectedCarrierLinePercent)
{
    public const int DefaultInvoicePercent = 100;
    public const int DefaultCreditNotePercent = 50;
    public const int DefaultEditedCarrierInvoicePercent = 50;
    public const int DefaultRejectedCarrierLinePercent = 25;

    /// <summary>Everything wrong with these shares; empty when they may be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (InvoicePercent is < 0 or > 100)
        {
            problems.Add("The share of Shippers Finance invoices is a percentage from 0 to 100.");
        }

        if (CreditNotePercent is < 0 or > 100)
        {
            problems.Add("The share of invoices Finance credits is a percentage from 0 to 100.");
        }

        if (EditedCarrierInvoicePercent is < 0 or > 100)
        {
            problems.Add("The share of Carrier invoices with an edited amount is a percentage from 0 to 100.");
        }

        if (RejectedCarrierLinePercent is < 0 or > 100)
        {
            problems.Add("The share of Carrier invoice lines Finance rejects is a percentage from 0 to 100.");
        }

        return problems;
    }
}
