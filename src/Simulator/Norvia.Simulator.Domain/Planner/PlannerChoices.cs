using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Domain.Planner;

/// <summary>
/// Makes the Planner's choices for a plan: exactly the share asked for, at positions the seed picks, as the bad share of
/// bookings is. Only bookings Norvia is expected to accept become Transport Orders to assign.
/// </summary>
public static class PlannerChoices
{
    // Apart from the bookings' own random sequence, so the bookings of a plan are the same whatever the Planner does.
    private const int PlannerSalt = 0x504C414E;

    public static IReadOnlyList<PlannerChoice> For(SimulationSettings settings, IReadOnlyList<PlannedBooking> bookings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bookings);

        var random = new SeededRandom(unchecked(settings.Seed ^ PlannerSalt));
        var orders = bookings.Where(booking => booking.Expected == ExpectedAnswer.Accepted).Select(booking => booking.Step).ToList();

        var assigned = Pick(random, orders, settings.AssignedPercent);
        var ownFleet = Pick(random, assigned, settings.OwnFleetPercent).ToHashSet();
        var reassigned = Pick(random, assigned, settings.ReassignPercent).ToHashSet();

        return
        [
            .. assigned.Select(step => new PlannerChoice(
                step,
                ownFleet.Contains(step) ? PlannerIntent.OwnFleetIfItFits : PlannerIntent.Carrier,
                random.Next(int.MaxValue),
                reassigned.Contains(step),
                random.Next(int.MaxValue))),
        ];
    }

    // Exactly the percentage of the items, picked by the seed, in their own order.
    private static List<int> Pick(SeededRandom random, List<int> items, int percent)
    {
        var wanted = (int)Math.Round(items.Count * percent / 100.0, MidpointRounding.AwayFromZero);
        var chosen = random.Shuffled(items.Count).Take(wanted).ToHashSet();
        return [.. items.Where((_, index) => chosen.Contains(index))];
    }
}
