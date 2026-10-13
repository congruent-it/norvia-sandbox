using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public static readonly SimulationLimits Limits = new(MaxOrdersPerRun: 50, MaxConcurrentRequests: 3);

    public static readonly SimulationWorld World = new(
    [
        new SimulatedShipper(
            "SHP-0001", "Brouwer Dranken", "brouwer-dranken", "Brouwer Dranken",
            [new RateZone("Rotterdam", "NL", ["30", "31"]), new RateZone("Antwerpen", "BE", ["20", "21"])],
            [new RateLane("Rotterdam", "Rotterdam"), new RateLane("Rotterdam", "Antwerpen")]),
        new SimulatedShipper(
            "SHP-0003", "Tuincentrum Groenrijk", "tuincentrum-groenrijk", "Tuincentrum Groenrijk",
            [new RateZone("Utrecht", "NL", ["34", "35"])],
            [new RateLane("Utrecht", "Utrecht")]),
    ]);

    public static SimulationSettings Settings(int orders = 6, int bad = 0, int seed = 11, RunMode mode = RunMode.Burst, int windowMinutes = 0) =>
        new(orders, bad, 30, seed, mode, TimeSpan.FromMinutes(windowMinutes));

    public static SimulationRun Run(SimulationSettings? settings = null, int number = 1)
    {
        settings ??= Settings();
        return SimulationRun.Start(number, settings, BookingPlanGenerator.Generate(settings, World, number), Now, "ngruson");
    }
}
