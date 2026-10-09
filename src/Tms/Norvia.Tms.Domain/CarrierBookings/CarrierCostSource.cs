using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>Where a Carrier cost came from. The names are stable: never rename one, only add.</summary>
public enum CarrierCostSourceKind
{
    /// <summary>Priced from the Carrier's Carrier Rate Card.</summary>
    CarrierRateCard = 1,

    /// <summary>A Spot price the Planner entered after the Carrier Rate Card failed to price the Shipment.</summary>
    Spot,
}

/// <summary>Where a Carrier cost came from: the Carrier Rate Card with the version that priced it, or a Spot price with its remark.</summary>
/// <param name="Remark">The Planner's remark; only a Spot price has one.</param>
public sealed record CarrierCostSource(CarrierCostSourceKind Kind, RateCardVersionReference? RateCardVersion, string? Remark = null)
{
    public const int MaxRemarkLength = 500;

    public static CarrierCostSource CarrierRateCard(RateCardVersionReference version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new(CarrierCostSourceKind.CarrierRateCard, version);
    }

    public static CarrierCostSource Spot(string remark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remark);
        return new(CarrierCostSourceKind.Spot, null, remark);
    }
}
