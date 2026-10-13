using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Domain.UnitTests.Plans;

internal static class TestWorld
{
    public static readonly SimulationWorld Benelux = new(
    [
        new SimulatedShipper(
            "SHP-0001", "Brouwer Dranken", "brouwer-dranken", "Brouwer Dranken",
            [
                new RateZone("Rotterdam", "NL", ["30", "31", "32"]),
                new RateZone("Antwerpen", "BE", ["20", "21", "22"]),
                new RateZone("Luxembourg", "LU", ["1", "2", "3"]),
            ],
            [new RateLane("Rotterdam", "Rotterdam"), new RateLane("Rotterdam", "Antwerpen"), new RateLane("Rotterdam", "Luxembourg")]),
        new SimulatedShipper(
            "SHP-0003", "Tuincentrum Groenrijk", "tuincentrum-groenrijk", "Tuincentrum Groenrijk",
            [new RateZone("Utrecht", "NL", ["34", "35"]), new RateZone("Amsterdam", "NL", ["10", "11"])],
            [new RateLane("Utrecht", "Amsterdam"), new RateLane("Utrecht", "Utrecht")]),
    ]);

    public static SimulationSettings Settings(int orders = 40, int badShare = 25, int seed = 7, int today = 30) =>
        new(orders, badShare, today, seed, RunMode.Burst, TimeSpan.Zero);
}
