using Norvia.Simulator.Domain.Carriers;
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
    ],
    [
        new SimulatedCarrier("CAR-0001", "Van Dijk Transport", true, "van-dijk-transport", "Van Dijk Transport", "VDT"),
        new SimulatedCarrier("CAR-0002", "Kessler Spedition", true, "kessler-spedition", "Kessler Spedition", "KES"),
        new SimulatedCarrier("CAR-0004", "Closed Haulage", false),
    ]);

    public static SimulationSettings Settings(
        int orders = 6, int bad = 0, int seed = 11, RunMode mode = RunMode.Burst, int windowMinutes = 0,
        int assigned = 0, int ownFleet = 50, int reassign = 0, CarrierShares? carriers = null) =>
        new(orders, bad, 30, seed, mode, TimeSpan.FromMinutes(windowMinutes), null, assigned, ownFleet, reassign)
        {
            // The Planner's tests don't go on to release what they assign, unless they ask for the Carrier stage.
            Carriers = carriers ?? new CarrierShares(ReleasePercent: 0),
        };

    public static SimulationRun Run(SimulationSettings? settings = null, int number = 1)
    {
        settings ??= Settings();
        return SimulationRun.Start(number, settings, BookingPlanGenerator.Generate(settings, World, number), Now, "ngruson");
    }

    public static readonly SimulatedVehicle Van = new("VEH-0001", "VN-101-A", VehicleKind.Van, 1200, 4);
    public static readonly SimulatedVehicle Truck = new("VEH-0003", "RT-201-C", VehicleKind.RigidTruck, 10000, 18);
    public static readonly SimulatedVehicle Tractor = new("VEH-0006", "TT-301-F", VehicleKind.TractorWithTrailer, 24000, 33);
    public static readonly SimulatedDriver Anouk = new("DRV-0001", "Anouk de Vries", [LicenceCategory.B, LicenceCategory.C, LicenceCategory.CE]);
    public static readonly SimulatedDriver Bram = new("DRV-0003", "Bram Visser", [LicenceCategory.B, LicenceCategory.C]);
    public static readonly SimulatedDriver Sanne = new("DRV-0006", "Sanne Jansen", [LicenceCategory.B]);

    /// <summary>The world with an Own fleet: three Vehicles and three Drivers, whose Shifts a test gives the days it needs.</summary>
    public static readonly SimulationWorld FleetWorld = new(World.Shippers, World.ActiveCarriers, [Van, Truck, Tractor], [Anouk, Bram, Sanne]);
}
