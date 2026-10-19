using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>
/// What Norvia pays a Carrier for one Carrier booking, in EUR without VAT, as the TMS froze it. The ERP keeps it as it came and
/// never recalculates it.
/// </summary>
public sealed record CarrierCostCharge(decimal Total, IReadOnlyList<ChargeLine> ChargeLines)
{
    public const string Currency = "EUR";

    public bool Equals(CarrierCostCharge? other) =>
        other is not null && Total == other.Total && ChargeLines.SequenceEqual(other.ChargeLines);

    public override int GetHashCode() => HashCode.Combine(Total, ChargeLines.Count);
}
