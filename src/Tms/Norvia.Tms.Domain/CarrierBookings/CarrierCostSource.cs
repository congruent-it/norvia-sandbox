using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Domain.CarrierBookings;

/// <summary>Where a Carrier cost came from. The names are stable: never rename one, only add.</summary>
public enum CarrierCostSourceKind
{
    /// <summary>Priced from the Carrier's Carrier Rate Card.</summary>
    CarrierRateCard = 1,
}

/// <summary>Where a Carrier cost came from: the Carrier Rate Card, with the version that priced it.</summary>
public sealed record CarrierCostSource(CarrierCostSourceKind Kind, RateCardVersionReference? RateCardVersion)
{
    public static CarrierCostSource CarrierRateCard(RateCardVersionReference version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new(CarrierCostSourceKind.CarrierRateCard, version);
    }
}
