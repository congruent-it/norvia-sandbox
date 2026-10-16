namespace Norvia.Simulator.Domain.Trips;

/// <summary>Picks which of the Own fleet's Shipments the Planner puts on Trips: exactly the share asked for, at positions the seed picks, so the same seed gives the same Shipments.</summary>
public static class TripSelection
{
    // Apart from the bookings', the Planner's, the Carriers' and the Drivers' own random sequences.
    private const int TripSalt = 0x54524950;

    /// <summary>For each of <paramref name="count"/> Shipments in plan order, whether it goes on a Trip.</summary>
    public static IReadOnlyList<bool> Pick(int seed, int count, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var wanted = (int)Math.Round(count * percent / 100.0, MidpointRounding.AwayFromZero);
        var chosen = new SeededRandom(unchecked(seed ^ TripSalt)).Shuffled(count).Take(Math.Min(wanted, count)).ToHashSet();
        return [.. Enumerable.Range(0, count).Select(chosen.Contains)];
    }
}
