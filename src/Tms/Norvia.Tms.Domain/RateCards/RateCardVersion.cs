using Norvia.Tms.Domain.TransportOrders;
using static System.FormattableString;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>
/// One version of a Rate Card, with the Zones and Lanes it prices. Its Zones may nest, a narrower postcode prefix
/// inside a wider one, but no two Zones may claim the same prefix in the same country.
/// </summary>
/// <param name="ValidFrom">The first day the version is valid.</param>
/// <param name="ValidTo">The last day the version is valid, or null when it has no end yet.</param>
public sealed record RateCardVersion(
    int Version,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    IReadOnlyList<Zone> Zones,
    IReadOnlyList<Lane> Lanes)
{
    private readonly IReadOnlyList<Zone> _zones = WithoutOverlaps(Zones);

    public IReadOnlyList<Zone> Zones
    {
        get => _zones;
        init => _zones = WithoutOverlaps(value);
    }

    public bool IsValidOn(DateOnly date) => date >= ValidFrom && (ValidTo is null || date <= ValidTo);

    /// <summary>
    /// Rates a Transport Order: its Freight Charge, or why this version can't price it because no Lane covers its
    /// Zones or its weight is above the Lane's highest Weight Break.
    /// </summary>
    /// <param name="cardName">What the card is called in a failure message: Rate Card or Carrier Rate Card.</param>
    /// <param name="rateCardId">The card this version belongs to, which the Freight Charge refers to.</param>
    internal RatingOutcome Rate(string cardName, RateCardId rateCardId, TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var origin = ZoneOf(details.Collection?.Address);
        var destination = ZoneOf(details.Delivery?.Address);
        if (Lanes.FirstOrDefault(lane => lane.Origin == origin?.Name && lane.Destination == destination?.Name) is not { } lane)
        {
            return RatingOutcome.Failed(RatingFailureCode.NoMatchingLane, Invariant(
                $"{cardName} {rateCardId} version {Version} has no Lane from {Describe(origin, details.Collection?.Address)} to {Describe(destination, details.Delivery?.Address)}."));
        }

        var weightKg = details.TotalWeightKg();
        return lane.ChargeLinesFor(weightKg) is { } chargeLines
            ? RatingOutcome.Priced(new FreightCharge(chargeLines, new RateCardVersionReference(rateCardId, Version)))
            : RatingOutcome.Failed(RatingFailureCode.WeightAboveHighestBreak, Invariant(
                $"The total weight of {weightKg} kg is above the highest Weight Break, up to {lane.HighestWeightBreakKg} kg, of the Lane from {lane.Origin} to {lane.Destination} in {cardName} {rateCardId} version {Version}."));
    }

    /// <summary>An end of a Lane the message names: its Zone, or the address when it lies in none.</summary>
    private static string Describe(Zone? zone, Address? address) =>
        zone is not null ? zone.Name.Value : Invariant($"{address?.Country} {address?.Postcode}, which lies in no Zone");

    /// <summary>
    /// The Zone the address lies in: of those that cover it, the one with the most specific postcode prefix. Null when
    /// it lies in none.
    /// </summary>
    private Zone? ZoneOf(Address? address) =>
        address is null
            ? null
            : Zones
                .Select(zone => (Zone: zone, Specificity: zone.SpecificityFor(address)))
                .Where(match => match.Specificity is not null)
                .OrderByDescending(match => match.Specificity)
                .Select(match => match.Zone)
                .FirstOrDefault();

    private static IReadOnlyList<Zone> WithoutOverlaps(IReadOnlyList<Zone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        var overlaps = zones
            .SelectMany((zone, index) => zones.Skip(index + 1).SelectMany(other =>
                zone.PrefixesSharedWith(other).Select(prefix => $"{zone.Name} and {other.Name} both claim {other.Country} {prefix}")))
            .ToList();

        return overlaps.Count == 0
            ? zones
            : throw new ArgumentException($"A Rate Card version's Zones overlap: {string.Join("; ", overlaps)}.", nameof(zones));
    }
}
