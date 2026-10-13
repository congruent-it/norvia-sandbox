using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Infrastructure.UnitTests;

internal static class TestPlans
{
    public static readonly SimulationWorld World = new(
    [
        new SimulatedShipper(
            "SHP-0001", "Brouwer Dranken", "brouwer-dranken", "Brouwer Dranken",
            [new RateZone("Rotterdam", "NL", ["30", "31"]), new RateZone("Antwerpen", "BE", ["20", "21"])],
            [new RateLane("Rotterdam", "Antwerpen"), new RateLane("Rotterdam", "Rotterdam")]),
    ]);

    public static SimulationPlan Plan(int orders = 40, int bad = 0, int seed = 5) =>
        BookingPlanGenerator.Generate(new SimulationSettings(orders, bad, 30, seed, RunMode.Burst, TimeSpan.Zero), World, 7);

    public static PlannedBooking Good() => Plan(orders: 1).Bookings[0];

    public static PlannedBooking Bad(BadBookingKind kind) =>
        Plan(orders: 200, bad: 60, seed: 9).Bookings.First(booking => booking.Bad == kind);
}
