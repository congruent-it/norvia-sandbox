namespace Norvia.PartnerPortal.Domain.CarrierInvoices;

/// <summary>What Norvia made of a Carrier invoice line when it received the invoice (E2). Anything but <see cref="Matched"/> waits for Norvia's Finance.</summary>
public enum CarrierInvoiceLineResult
{
    Matched,
    PriceMismatch,
    UnknownBooking,
    Duplicate,
}

public static class CarrierInvoiceLineResults
{
    /// <summary>The result the Carrier invoice response format spells so, or false for any other text.</summary>
    public static bool TryParse(string? wire, out CarrierInvoiceLineResult result)
    {
        (var known, result) = wire switch
        {
            "MATCHED" => (true, CarrierInvoiceLineResult.Matched),
            "PRICE_MISMATCH" => (true, CarrierInvoiceLineResult.PriceMismatch),
            "UNKNOWN_BOOKING" => (true, CarrierInvoiceLineResult.UnknownBooking),
            "DUPLICATE" => (true, CarrierInvoiceLineResult.Duplicate),
            _ => (false, default),
        };
        return known;
    }
}
