using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.RateCards;

/// <summary>Maps the plain values to the domain's versions, and back for the replica store.</summary>
public static class RateCardVersionMapper
{
    /// <exception cref="ArgumentException">A name is blank or two Zones claim the same postcode prefix.</exception>
    public static IReadOnlyList<RateCardVersion> ToDomain(IReadOnlyList<RateCardVersionData> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return
        [
            .. versions.Select(version => new RateCardVersion(
                version.Version,
                version.ValidFrom,
                version.ValidTo,
                [.. (version.Zones ?? []).Select(zone => new Zone(new ZoneName(zone.Name), zone.Country, [.. zone.PostcodePrefixes ?? []]))],
                [.. (version.Lanes ?? []).Select(lane => new Lane(
                    new ZoneName(lane.Origin),
                    new ZoneName(lane.Destination),
                    [.. (lane.WeightBreaks ?? []).Select(weightBreak => new WeightBreak(weightBreak.UpToKg, new Money(weightBreak.PricePer100Kg)))],
                    new Money(lane.MinimumCharge)))])),
        ];
    }
}
