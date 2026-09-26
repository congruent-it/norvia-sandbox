using Norvia.Tms.Domain.TransportOrders;

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
    /// The charge lines for a Transport Order, each rounded to cents, or null when no Lane or Weight Break covers it.
    /// </summary>
    public IReadOnlyList<ChargeLine>? ChargeLinesFor(TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var origin = ZoneOf(details.Collection?.Address);
        var destination = ZoneOf(details.Delivery?.Address);
        var lane = Lanes.FirstOrDefault(lane => lane.Origin == origin?.Name && lane.Destination == destination?.Name);

        return lane?.ChargeLinesFor(details.TotalWeightKg());
    }

    /// <summary>The Zone the address lies in: of those that cover it, the one with the most specific postcode prefix.</summary>
    private Zone? ZoneOf(Address? address) =>
        address is null
            ? null
            : Zones
                .Select(zone => (Zone: zone, Specificity: zone.SpecificityFor(address)))
                .Where(match => match.Specificity is not null)
                .MaxBy(match => match.Specificity)
                .Zone;

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
