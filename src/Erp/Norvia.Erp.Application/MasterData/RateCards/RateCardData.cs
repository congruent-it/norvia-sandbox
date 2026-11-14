using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.MasterData.RateCards;

internal static class RateCardData
{
    public static IReadOnlyList<RateCardVersion> ToDomain(IReadOnlyList<RateCardVersionData> versions) =>
        [.. versions.Select(version => new RateCardVersion(
            version.Version,
            version.ValidFrom,
            version.ValidTo,
            [.. (version.Zones ?? []).Select(zone => new Zone(zone.Name, zone.Country, [.. zone.PostcodePrefixes ?? []]))],
            [.. (version.Lanes ?? []).Select(lane => new Lane(
                lane.Origin,
                lane.Destination,
                lane.MinimumCharge,
                [.. (lane.WeightBreaks ?? []).Select(weightBreak => new WeightBreak(weightBreak.UpToKg, weightBreak.PricePer100Kg))]))]))];

    public static IReadOnlyList<RateCardVersionData> Of(IReadOnlyList<RateCardVersion> versions) =>
        [.. RateCardVersionRules.Ordered(versions).Select(version => new RateCardVersionData(
            version.Version,
            version.ValidFrom,
            version.ValidTo,
            [.. version.Zones.Select(zone => new ZoneData(zone.Name, zone.Country, zone.PostcodePrefixes))],
            [.. version.Lanes.Select(lane => new LaneData(
                lane.Origin,
                lane.Destination,
                lane.MinimumCharge,
                [.. lane.WeightBreaks.Select(weightBreak => new WeightBreakData(weightBreak.UpToKg, weightBreak.PricePer100Kg))]))]))];
}
