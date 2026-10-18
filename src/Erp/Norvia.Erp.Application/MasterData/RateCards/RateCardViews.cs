using Norvia.Erp.Domain.RateCards;

namespace Norvia.Erp.Application.MasterData.RateCards;

internal static class RateCardViews
{
    public static IReadOnlyList<RateCardVersionView> Of(IReadOnlyList<RateCardVersion> versions) =>
        [.. versions.OrderBy(version => version.Version).Select(version => new RateCardVersionView(
            version.Version,
            version.ValidFrom,
            version.ValidTo,
            version.Zones.Count,
            [.. version.Lanes.Select(lane => new LaneView(lane.Origin, lane.Destination, lane.MinimumCharge, lane.WeightBreaks.Count))]))];
}
