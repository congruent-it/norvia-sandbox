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

    /// <summary>How the Carrier invoice outcome format spells a result.</summary>
    public static string WireNameOf(CarrierInvoiceOutcomeResult result) => result switch
    {
        CarrierInvoiceOutcomeResult.Matched => "MATCHED",
        CarrierInvoiceOutcomeResult.Accepted => "ACCEPTED",
        CarrierInvoiceOutcomeResult.AcceptedAdjusted => "ACCEPTED_ADJUSTED",
        CarrierInvoiceOutcomeResult.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Every result has a wire name."),
    };

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
