namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>What became of a Carrier invoice line, as Norvia tells the Carrier once the invoice is Approved (E2).</summary>
public enum CarrierInvoiceOutcomeResult
{
    Matched,
    Accepted,
    AcceptedAdjusted,
    Rejected,
}

public static class CarrierInvoiceOutcomeResults
{
    public static IReadOnlyList<string> WireNames { get; } = ["MATCHED", "ACCEPTED", "ACCEPTED_ADJUSTED", "REJECTED"];

    /// <summary>The result the Carrier invoice outcome format spells so, or false for any other text.</summary>
    public static bool TryParse(string? wire, out CarrierInvoiceOutcomeResult result)
    {
        (var known, result) = wire switch
        {
            "MATCHED" => (true, CarrierInvoiceOutcomeResult.Matched),
            "ACCEPTED" => (true, CarrierInvoiceOutcomeResult.Accepted),
            "ACCEPTED_ADJUSTED" => (true, CarrierInvoiceOutcomeResult.AcceptedAdjusted),
            "REJECTED" => (true, CarrierInvoiceOutcomeResult.Rejected),
            _ => (false, default),
        };
        return known;
    }
}
