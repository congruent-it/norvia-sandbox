namespace Norvia.Erp.Web.ShipperInvoices;

/// <summary>What <b>Send again</b> did for a Not delivered Shipper invoice.</summary>
internal abstract record SendAgainOutcome
{
    /// <summary>The invoice is Issued once more and was published again.</summary>
    public sealed record SentAgain(string InvoiceNumber) : SendAgainOutcome;

    /// <summary>The invoice wasn't sent again, and the reason says why.</summary>
    public sealed record Refused(string Reason) : SendAgainOutcome;
}
