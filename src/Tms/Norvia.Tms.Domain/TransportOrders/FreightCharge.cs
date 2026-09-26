using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The price Norvia charges a Shipper for one Transport Order, as calculated by Rating: its charge lines, in EUR without
/// VAT, and the Rate Card version it was calculated from. Once frozen on a Transport Order it never changes.
/// </summary>
public sealed record FreightCharge(IReadOnlyList<ChargeLine> ChargeLines, RateCardVersionReference RateCardVersion)
{
    /// <summary>Rating charges in euros only; the ERP adds VAT when it invoices.</summary>
    public const string Currency = "EUR";

    /// <summary>The sum of the charge lines, each already rounded to cents.</summary>
    public decimal Total => ChargeLines.Sum(line => line.Amount);

    public bool Equals(FreightCharge? other) =>
        other is not null && RateCardVersion == other.RateCardVersion && ChargeLines.SequenceEqual(other.ChargeLines);

    public override int GetHashCode() => HashCode.Combine(RateCardVersion, ChargeLines.Count);
}
