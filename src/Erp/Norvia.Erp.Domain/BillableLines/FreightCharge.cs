namespace Norvia.Erp.Domain.BillableLines;

/// <summary>
/// The price the TMS froze for a Transport Order, as the ERP received it. The ERP keeps it as it came and never recalculates it.
/// </summary>
public sealed record FreightCharge(decimal Total, IReadOnlyList<ChargeLine> ChargeLines, string RateCardId, int RateCardVersion)
{
    public const string Currency = "EUR";

    public bool Equals(FreightCharge? other) =>
        other is not null
        && Total == other.Total
        && RateCardId == other.RateCardId
        && RateCardVersion == other.RateCardVersion
        && ChargeLines.SequenceEqual(other.ChargeLines);

    public override int GetHashCode() => HashCode.Combine(Total, RateCardId, RateCardVersion, ChargeLines.Count);
}
