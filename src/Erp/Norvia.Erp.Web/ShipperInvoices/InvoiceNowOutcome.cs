namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>What <b>Invoice now</b> did for a Shipper.</summary>
internal abstract record InvoiceNowOutcome
{
    /// <summary>One Shipper invoice was issued.</summary>
    public sealed record Issued(string InvoiceNumber, int LineCount) : InvoiceNowOutcome;

    /// <summary>The Shipper had no Waiting lines, so it got no invoice.</summary>
    public sealed record NothingWaiting : InvoiceNowOutcome;

    /// <summary>The ERP didn't issue an invoice, and says why.</summary>
    public sealed record Failed(string Reason) : InvoiceNowOutcome;
}
