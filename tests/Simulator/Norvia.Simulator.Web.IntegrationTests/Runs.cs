extern alias SimulatorWeb;

using Ardalis.Result;
using Norvia.Simulator.Application.Runs;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>What the tests do with the Simulator's runs, through the doors its pages use.</summary>
internal static class Runs
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static int _seed = Environment.TickCount;

    /// <summary>A seed of this test's own, so no two tests make the same plan.</summary>
    public static int NextSeed() => Interlocked.Increment(ref _seed);

    public static SimulationSettings Burst(int orders, int badSharePercent, int seed) =>
        new(orders, badSharePercent, CollectedTodayPercent: 30, seed, RunMode.Burst, TimeSpan.Zero);

    /// <summary>A run of good bookings whose Planner assigns, sends to Own fleet and reassigns the given shares of the orders.</summary>
    public static SimulationSettings Planned(int orders, int seed, int assignedPercent, int ownFleetPercent, int reassignPercent, int badSharePercent = 0) =>
        new(orders, badSharePercent, CollectedTodayPercent: 30, seed, RunMode.Burst, TimeSpan.Zero,
            AssignedPercent: assignedPercent, OwnFleetPercent: ownFleetPercent, ReassignPercent: reassignPercent)
        {
            // These runs test the Planner: the Shipments stay Planned, and the Carrier stage has its own tests (#354).
            Carriers = new Norvia.Simulator.Domain.Carriers.CarrierShares(ReleasePercent: 0),
        };

    /// <summary>A run that sends its first booking at once and the next a minute later.</summary>
    public static SimulationSettings Paced(int orders, int seed) =>
        new(orders, BadSharePercent: 0, CollectedTodayPercent: 30, seed, RunMode.Paced, TimeSpan.FromMinutes(orders));

    public static async Task<RunSummary> StartAsync(this ISimulatorRequests simulator, SimulationSettings settings, CancellationToken cancellationToken)
    {
        var started = await simulator.StartAsync(settings, "integration tests", cancellationToken);
        started.Status.Should().Be(ResultStatus.Ok, string.Join("; ", started.Errors));
        return started.Value;
    }

    /// <summary>Waits until the run is no longer active.</summary>
    public static Task<RunSummary> FinishedAsync(this ISimulatorRequests simulator, Guid runId, CancellationToken cancellationToken) =>
        EventuallyAsync(async () =>
        {
            var run = (await simulator.GetRunAsync(new SimulationRunId(runId), cancellationToken)).Value.Run;
            return run.IsActive ? null : run;
        }, cancellationToken);

    /// <summary>Waits until the run's log has a line that satisfies the predicate, and returns the log.</summary>
    public static Task<IReadOnlyList<LogLine>> LogWithAsync(
        this ISimulatorRequests simulator, Guid runId, Func<LogLine, bool> predicate, CancellationToken cancellationToken) =>
        EventuallyAsync(async () =>
        {
            var log = await simulator.ListLogAsync(new SimulationRunId(runId), new LogFilter(), cancellationToken);
            return log.Any(predicate) ? log : null;
        }, cancellationToken);

    public static async Task<IReadOnlyList<LogLine>> LogAsync(this ISimulatorRequests simulator, Guid runId, CancellationToken cancellationToken) =>
        await simulator.ListLogAsync(new SimulationRunId(runId), new LogFilter(), cancellationToken);

    /// <summary>The lines in which a Persona sent a booking, as opposed to the Simulator's own notes.</summary>
    public static bool IsBooking(LogLine line) => LogOutcomes.Booking.Contains(line.Outcome);

    /// <summary>The lines in which the Planner called the TMS API.</summary>
    public static bool IsPlanning(LogLine line) => LogOutcomes.Planning.Contains(line.Outcome);

    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> attempt, CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Patience);
        while (true)
        {
            if (await attempt() is { } found)
            {
                return found;
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }
}
