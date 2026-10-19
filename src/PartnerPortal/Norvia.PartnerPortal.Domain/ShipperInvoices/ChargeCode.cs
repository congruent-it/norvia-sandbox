namespace Norvia.PartnerPortal.Domain.ShipperInvoices;

/// <summary>What a charge line on an invoice is for, as the Shipper invoice format names it (E1): <c>FREIGHT</c> or <c>MINIMUM_CHARGE_ADJUSTMENT</c>.</summary>
public enum ChargeCode
{
    Freight,
    MinimumChargeAdjustment,
}

public static class ChargeCodes
{
    public static IReadOnlyList<string> WireNames { get; } = [.. Enum.GetValues<ChargeCode>().Select(WireNameOf)];

    public static string WireNameOf(ChargeCode code) => InvoiceWireName.Of(code);

    /// <summary>The code the format spells so, or false for any other text.</summary>
    public static bool TryParse(string? wire, out ChargeCode code) => InvoiceWireName.TryParse(wire, out code);
}
