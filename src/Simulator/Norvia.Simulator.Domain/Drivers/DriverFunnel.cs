namespace Norvia.Simulator.Domain.Drivers;

/// <summary>
/// Makes the journeys of the Shipments on today's Trips: exactly the share asked for at each step, at positions the seed picks, so the same seed and the same
/// number of Shipments give the same journeys. It is worked out once the Trips are built, when it is known how many Shipments run today.
/// </summary>
public static class DriverFunnel
{
    // Apart from the bookings', the Planner's and the Carriers' own random sequences.
    private const int DriverSalt = 0x44525652;

    public static IReadOnlyList<DriverJourney> Plan(DriverShares shares, int seed, int shipmentCount)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentOutOfRangeException.ThrowIfNegative(shipmentCount);

        var random = new SeededRandom(unchecked(seed ^ DriverSalt));
        var all = Enumerable.Range(0, shipmentCount).ToList();

        var collectionFailed = Pick(random, all, shares.CollectionFailedPercent, shipmentCount);
        var collected = all.Except(collectionFailed).ToList();
        var deliveryFailed = Pick(random, collected, shares.DeliveryFailedPercent, shipmentCount);
        var damaged = Pick(random, all, shares.DamagedPercent, shipmentCount);
        var delivered = collected.Except(deliveryFailed).ToList();
        var reservations = Pick(random, delivered, shares.ReservationsPercent, delivered.Count);

        var collectionFailedSet = collectionFailed.ToHashSet();
        var deliveryFailedSet = deliveryFailed.ToHashSet();
        var damagedSet = damaged.ToHashSet();
        var reservationsSet = reservations.ToHashSet();
        return
        [
            .. all.Select(index => new DriverJourney(
                index, collectionFailedSet.Contains(index), deliveryFailedSet.Contains(index), damagedSet.Contains(index), reservationsSet.Contains(index))),
        ];
    }

    public static DriverFunnelCounts Count(IReadOnlyList<DriverJourney> journeys)
    {
        ArgumentNullException.ThrowIfNull(journeys);

        return new DriverFunnelCounts(
            journeys.Count,
            journeys.Count(journey => journey.CollectionFailed),
            journeys.Count(journey => journey.DeliveryFailed),
            journeys.Count(journey => journey.Damaged),
            journeys.Count(journey => journey.Delivered),
            journeys.Count(journey => journey.Reservations));
    }

    // Exactly the percentage of ofCount (at most what there is), picked by the seed, in the items' own order.
    private static List<int> Pick(SeededRandom random, List<int> items, int percent, int ofCount)
    {
        var wanted = (int)Math.Round(ofCount * percent / 100.0, MidpointRounding.AwayFromZero);
        var chosen = random.Shuffled(items.Count).Take(Math.Min(wanted, items.Count)).ToHashSet();
        return [.. items.Where((_, position) => chosen.Contains(position))];
    }
}
