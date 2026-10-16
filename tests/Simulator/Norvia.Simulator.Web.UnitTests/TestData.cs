using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Application.Runs.Open;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    public static readonly SimulationLimits Limits = new(MaxOrdersPerRun: 50, MaxConcurrentRequests: 3);

    public static RunSummary Summary(SimulationRunStatus status = SimulationRunStatus.Running, int number = 4, int sent = 3, int planned = 10) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), number, status, Now, status == SimulationRunStatus.Running ? null : Now.AddMinutes(2),
            "Dev Eloper", 77, "Quiet Monday", RunMode.Burst, planned, sent);

    public static SimulationSettings Settings(int seed = 77, string? preset = "Quiet Monday", RunMode mode = RunMode.Burst) =>
        new(10, 10, 30, seed, mode, mode == RunMode.Paced ? TimeSpan.FromMinutes(15) : TimeSpan.Zero, preset, 70, 40, 5)
        {
            TripPercent = 90,
            Carriers = new CarrierShares(60, 85, 10, 75, 20, 90, 65, 50),
            Drivers = new DriverShares(8, 6, 4, 12),
        };

    public static RunDetail Detail(RunSummary? summary = null, SimulationSettings? settings = null) =>
        new(summary ?? Summary(), [new OutcomeCount(LogOutcome.Accepted, 2), new OutcomeCount(LogOutcome.Rejected, 1)], ["Brouwer Dranken", "Tuincentrum Groenrijk"], settings ?? Settings());
}
