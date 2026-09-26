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
    /// The Freight for a Transport Order, rounded to cents, or null when no Lane or Weight Break covers it.
    /// </summary>
    public Money? FreightFor(TransportOrderDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var origin = ZoneOf(details.Collection?.Address);
        var destination = ZoneOf(details.Delivery?.Address);
        var lane = Lanes.FirstOrDefault(lane => lane.Origin == origin?.Name && lane.Destination == destination?.Name);
        var weightKg = details.TotalWeightKg();

        return lane?.WeightBreakFor(weightKg) is { } weightBreak
            ? weightBreak.PricePer100Kg.Times(weightKg / 100m).RoundedToCents()
            : null;
    }

    private Zone? ZoneOf(Address? address) => address is null ? null : Zones.FirstOrDefault(zone => zone.Covers(address));
}
