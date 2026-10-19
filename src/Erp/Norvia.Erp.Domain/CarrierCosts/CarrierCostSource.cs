namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>
/// Where the TMS got a Carrier cost: a Carrier Rate Card version, or a Spot price with the Planner's remark. Exactly one of the two
/// is present, as the Canonical Model has it.
/// </summary>
public sealed record CarrierCostSource
{
    private CarrierCostSource(CarrierCostSourceKind kind, string? rateCardId, int? rateCardVersion, string? remark)
    {
        Kind = kind;
        RateCardId = rateCardId;
        RateCardVersion = rateCardVersion;
        Remark = remark;
    }

    public CarrierCostSourceKind Kind { get; }

    public string? RateCardId { get; }

    public int? RateCardVersion { get; }

    /// <summary>The Planner's remark with the Spot price.</summary>
    public string? Remark { get; }

    public bool IsSpot => Kind == CarrierCostSourceKind.Spot;

    public static CarrierCostSource FromRateCard(string rateCardId, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rateCardId);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        return new(CarrierCostSourceKind.CarrierRateCard, rateCardId, version, null);
    }

    public static CarrierCostSource FromSpot(string remark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remark);
        return new(CarrierCostSourceKind.Spot, null, null, remark);
    }

    /// <summary>For the stored document only: restores a source exactly as it was kept.</summary>
    public static CarrierCostSource Restore(CarrierCostSourceKind kind, string? rateCardId, int? rateCardVersion, string? remark) =>
        new(kind, rateCardId, rateCardVersion, remark);
}
