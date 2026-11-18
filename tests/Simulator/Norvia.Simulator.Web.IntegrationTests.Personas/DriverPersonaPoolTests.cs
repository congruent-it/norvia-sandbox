extern alias SimulatorWeb;

using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The pools of Driver texts (#486) at the Simulator's boundary, with the Agents on: the personas agent is the in-process runner with the scripted fake model,
/// so the pools are deterministic, and the Driver app API, the Logic Apps' stand-ins and the TMS are the real ones. The Own fleet's Driver of the run is
/// DRV-0002, whose profile is terse Dutch.
/// </summary>
/// <remarks>
/// The TMS gives a Driver and a Vehicle one Trip a day, and every other Driver and Vehicle of the seed is another test class's, so the cases of this class read
/// one run back (<see cref="DriverTextRun"/>) instead of each making their own. None of them changes it, so their order does not matter.
/// </remarks>
[Collection("Simulator runs")]
public sealed class DriverPersonaPoolTests(SimulatorFixture fixture, DriverTextRun run) : IClassFixture<DriverTextRun>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACollectionFailed_ReachesTheTms_WithARemarkFromTheDriversPool()
    {
        var reports = await run.ReportsAsync(DriverActions.CollectionFailed, TextCategory.DriverCollectionFailedRemark, Ct);

        reports.Should().NotBeEmpty("a share of the Stops fails at the collection");
        await AssertRemarksReachTheTmsAsync(reports);
    }

    [Fact]
    public async Task ADeliveryFailed_ReachesTheTms_WithARemarkFromTheDriversPool()
    {
        var reports = await run.ReportsAsync(DriverActions.DeliveryFailed, TextCategory.DriverDeliveryFailedRemark, Ct);

        reports.Should().NotBeEmpty("a share of the collected Shipments fails at the delivery");
        await AssertRemarksReachTheTmsAsync(reports);
    }

    [Fact]
    public async Task ADamageReport_ReachesTheTms_WithARemarkFromTheDriversPool()
    {
        var reports = await run.ReportsAsync(DriverActions.ReportDamage, TextCategory.DriverDamageRemark, Ct);

        reports.Should().NotBeEmpty("a share of the Shipments is found damaged");
        await AssertRemarksReachTheTmsAsync(reports);
    }

    [Fact]
    public async Task TheTexts_AreInTheLanguageOfTheDriversProfile()
    {
        var plan = await run.PlanAsync(Ct);

        plan.DriverProfiles["DRV-0002"].Should().Be("terse-dutch");
        foreach (var (action, category) in run.Kinds)
        {
            var reports = await run.ReportsAsync(action, category, Ct);
            reports.Should().NotBeEmpty();
            reports.Should().OnlyContain(report => report.Pool.ProfileId == "terse-dutch" && report.Pool.Language == "nl", $"the {category} texts are Dutch");
            reports.Should().OnlyContain(report => report.Pool.Category == category);
        }
    }

    [Fact]
    public async Task ARecordedPlan_ReplaysTheSameDriverTexts_WithNoModel()
    {
        var recorded = await run.PlanAsync(Ct);
        recorded.Pools.Where(pool => TextCategories.IsDriverText(pool.Category)).Should().NotBeEmpty();

        // A Simulator of its own whose model is gone: any call to the agent is counted, and the test expects none.
        var calls = 0;
        await using var withoutModel = await fixture.StartSimulatorWithAgentsAsync(
            services => services.AddSingleton<IAgentClient>(new FailingAgentClient(() => Interlocked.Increment(ref calls))), Ct);
        var replayed = (await RunAsync(withoutModel, run.RunId)).Plan;

        replayed.Pools.Should().BeEquivalentTo(recorded.Pools, "the plan keeps its pools in the Simulator's database");
        replayed.DriverProfiles.Should().BeEquivalentTo(recorded.DriverProfiles);
        calls.Should().Be(0, "reading a recorded plan asks no model");

        // What the run sent is what the recorded pools draw for the same seed and steps.
        foreach (var (action, category) in run.Kinds)
        {
            foreach (var report in await run.ReportsAsync(action, category, Ct))
            {
                var pool = replayed.Pools.Single(candidate => candidate.Name == report.Pool.Name);
                pool.Texts[report.Index].Should().Be(report.Text);
            }
        }
    }

    [Fact]
    public async Task TheSameSeed_GivesTheSameDriverPools()
    {
        // Previewing writes the pools without sending anything, so a Simulator of its own can preview the run's settings again.
        await using var simulator = await fixture.StartSimulatorWithAgentsAsync(
            configureServices: null, Ct, SeedEdits.OnlyShippers(DriverTextRun.Shipper), SeedEdits.OwnFleetOf(DriverTextRun.Vehicle, DriverTextRun.Driver));

        var first = (await simulator.Requests.PreviewAsync(run.Settings, Ct)).Value;
        var second = (await simulator.Requests.PreviewAsync(run.Settings, Ct)).Value;

        var pools = first.Pools.Where(pool => pool.IsDriverText).ToList();
        pools.Select(pool => pool.Category).Should().BeEquivalentTo(TextCategories.ForDrivers.Select(category => category.ToString()));
        pools.Should().OnlyContain(pool => pool.Source.StartsWith("agent, run ", StringComparison.Ordinal) && pool.ProfileId == "terse-dutch" && pool.Language == "nl");
        pools.Should().OnlyContain(pool => pool.DriverIds!.Contains(DriverTextRun.Driver));
        second.Pools.Where(pool => pool.IsDriverText).Should().BeEquivalentTo(pools, options => options.Excluding(pool => pool.Source), "the same seed writes the same pools");
    }

    [Fact]
    public async Task NoDriverText_IsInTheLogOrTheTelemetry_AndTheLogNamesAPoolAndAnIndex()
    {
        var plan = await run.PlanAsync(Ct);
        var log = await run.LogAsync(Ct);
        var texts = plan.Pools.Where(pool => TextCategories.IsDriverText(pool.Category)).SelectMany(pool => pool.Texts).Distinct().ToList();
        texts.Should().NotBeEmpty();

        var seen = log.Select(line => $"{line.Action} {line.Detail}").Concat(run.Simulator.Telemetry.Entries).ToList();
        foreach (var text in texts)
        {
            seen.Should().NotContain(entry => entry.Contains(text, StringComparison.Ordinal), "the log and the telemetry name a pool and an index, never a text");
        }

        foreach (var (action, _) in run.Kinds)
        {
            log.Where(line => line.Action == action && line.Outcome == LogOutcome.DriverReported).Should()
                .NotBeEmpty().And.OnlyContain(line => line.Detail!.Contains("pool ", StringComparison.Ordinal) && line.Detail.Contains(", index ", StringComparison.Ordinal));
        }

        // The Driver app API and the TMS keep the Drivers' remarks, but their logs don't repeat them.
        foreach (var resource in new[] { "driver-app-api", "tms-api" })
        {
            var logs = await fixture.ResourceLogsAsync(resource, Ct);
            foreach (var text in texts)
            {
                logs.Should().NotContain(text, $"the {resource} logs no remark of a Driver");
            }
        }
    }

    // Each remark is on the Shipment's timeline in the TMS, as the Driver sent it, which is the one the pool holds at the logged index.
    private async Task AssertRemarksReachTheTmsAsync(IReadOnlyList<DriverText> reports)
    {
        foreach (var report in reports)
        {
            await Runs.WaitUntilAsync(async () => (await fixture.TimelineAsync(report.ShipmentNumber, Ct))!["trackingEvents"]!.AsArray()
                .Any(entry => entry!["remark"]?.GetValue<string>() == report.Text), Ct);
        }
    }

    private static async Task<SimulationRun> RunAsync(SimulatorHost simulator, Guid runId)
    {
        await using var scope = simulator.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IReadRepository<SimulationRun>>().GetByIdAsync(new SimulationRunId(runId), Ct))!;
    }

    internal sealed class FailingAgentClient(Action called) : IAgentClient
    {
        public Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken)
        {
            called();
            return Task.FromResult(Result<IReadOnlyList<string>>.Unavailable("agent-unavailable"));
        }
    }
}

/// <summary>A text a Driver sent: the Shipment it was about, the pool and the index the log names, and the text that index holds in the plan's pool.</summary>
public sealed record DriverText(string ShipmentNumber, TextPool Pool, int Index)
{
    public string Text => Pool.Texts[Index];
}
