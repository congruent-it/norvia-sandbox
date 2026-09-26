using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.RateCards;

/// <summary>One version of a Rate Card, with the Zones and Lanes it prices.</summary>
/// <param name="ValidFrom">The first day the version is valid.</param>
/// <param name="ValidTo">The last day the version is valid, or null when it has no end yet.</param>
public sealed record RateCardVersion(
    int Version,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    IReadOnlyList<Zone> Zones,
    IReadOnlyList<Lane> Lanes)
{
    public bool IsValidOn(DateOnly date) => date >= ValidFrom && (ValidTo is null || date <= ValidTo);

    /// <summary>
    /// The Freight for a Transport Order in EUR, rounded to cents half away from zero, or null when no Lane or Weight
    /// Break covers it.
    /// </summary>
    public decimal? FreightFor(TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var origin = ZoneOf(details.Collection?.Address);
        var destination = ZoneOf(details.Delivery?.Address);
        var lane = Lanes.FirstOrDefault(lane => lane.Origin == origin?.Name && lane.Destination == destination?.Name);
        var weightKg = details.GoodsLines.Sum(line => line.WeightKg ?? 0m);

        return lane?.WeightBreakFor(weightKg) is { } weightBreak
            ? Math.Round(weightKg / 100m * weightBreak.PricePer100Kg, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    private Zone? ZoneOf(Address? address) => address is null ? null : Zones.FirstOrDefault(zone => zone.Covers(address));
}
