using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>A version of a Rate Card or a Carrier Rate Card as the seed file spells it, which have the same shape.</summary>
internal sealed record SeedRateCardVersion(
    int Version,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    IReadOnlyList<SeedZone> Zones,
    IReadOnlyList<SeedLane> Lanes);

internal sealed record SeedZone(string Name, string Country, IReadOnlyList<string> PostcodePrefixes);

internal sealed record SeedLane(string Origin, string Destination, IReadOnlyList<SeedWeightBreak> WeightBreaks, decimal? MinimumCharge);

internal sealed record SeedWeightBreak(decimal UpToKg, decimal PricePer100Kg);

internal static class SeedRateCardVersions
{
    /// <summary>
    /// Maps a seed version to the domain's. A version whose Zones overlap, or a Lane without a minimum charge, is a
    /// broken seed.
    /// </summary>
    /// <param name="cardName">What the card is called in the error: Rate Card or Carrier Rate Card.</param>
    public static RateCardVersion ToVersion(SeedRateCardVersion version, string cardName, string cardId, string path)
    {
        try
        {
            return new RateCardVersion(
                version.Version,
                version.ValidFrom,
                version.ValidTo,
                [.. version.Zones.Select(zone => new Zone(new ZoneName(zone.Name), zone.Country, zone.PostcodePrefixes))],
                [.. version.Lanes.Select(lane => new Lane(
                    new ZoneName(lane.Origin),
                    new ZoneName(lane.Destination),
                    [.. lane.WeightBreaks.Select(weightBreak => new WeightBreak(weightBreak.UpToKg, new Money(weightBreak.PricePer100Kg)))],
                    new Money(lane.MinimumCharge
                        ?? throw new InvalidDataException($"The Lane from {lane.Origin} to {lane.Destination} has no minimum charge."))))]);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"{cardName} {cardId} version {version.Version} in the seed file '{path}' is invalid. {exception.Message}", exception);
        }
    }
}
