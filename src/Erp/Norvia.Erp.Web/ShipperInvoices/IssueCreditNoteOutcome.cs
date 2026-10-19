namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>What <b>Credit note</b> did for an invoice line.</summary>
internal abstract record IssueCreditNoteOutcome
{
    /// <summary>The Credit note was issued and published.</summary>
    public sealed record Issued(string CreditNoteNumber) : IssueCreditNoteOutcome;

    /// <summary>The Credit note wasn't issued, and the reason says why.</summary>
    public sealed record Refused(string Reason) : IssueCreditNoteOutcome;
}
