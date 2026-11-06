extern alias SimulatorWeb;

using System.Text.RegularExpressions;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The pools of Carrier texts (#484) at the Simulator's boundary, with the Agents on: the personas agent is the in-process runner with the scripted
/// fake model, so the pools are deterministic and the Partner API and the TMS are the real ones.
/// </summary>
[Collection("Simulator runs")]
public sealed partial class PersonaPoolTests(SimulatorFixture fixture)
{
    // The Shipper whose every Lane the Carriers' Rate Cards cover, so the TMS can price each release.
    private const string PricedShipper = "SHP-0001";

    private static readonly CarrierShares Declines = new(
        ReleasePercent: 100, ConfirmPercent: 0, DeclinePercent: 100, TrackedPercent: 0, ExceptionPercent: 0,
        DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);

    // Every Shipment is tracked and hits an Exception; the Exceptions are dealt in turn, so some are failed deliveries.
    private static readonly CarrierShares Exceptions = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 100,
        DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"pool (?<pool>\S+), index (?<index>\d+)")]
    private static partial Regex PoolReference();

    [Fact]
    public async Task TheSameSeed_GivesTheSamePools_AndTheSameDrawnTexts()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 12, Runs.NextSeed(), Declines);

        var (firstPreview, firstRun, firstLog) = await simulator.RunToEndAsync(settings, Ct);
        var (secondPreview, secondRun, secondLog) = await simulator.RunToEndAsync(settings, Ct);

        firstPreview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => pool.Source.StartsWith("agent, run ", StringComparison.Ordinal));
        secondPreview.Pools.Should().BeEquivalentTo(firstPreview.Pools, options => options.Excluding(pool => pool.Source), "the same seed writes the same pools");
        (await PlanOfAsync(simulator, firstRun.Id)).Pools.Select(pool => (pool.Name, pool.Texts)).Should().BeEquivalentTo(
            (await PlanOfAsync(simulator, secondRun.Id)).Pools.Select(pool => (pool.Name, pool.Texts)));

        var first = References(firstLog);
        first.Should().NotBeEmpty();
        References(secondLog).Should().Equal(first, "the same seed draws the same text for the same step");
    }

    [Fact]
    public async Task ARecordedPlan_ReplaysExactly_WithNoModel()
    {
        await using var recording = await PricedSimulatorAsync();
        var (_, run, log) = await recording.RunToEndAsync(Runs.Carried(orders: 12, Runs.NextSeed(), Declines), Ct);
        var recorded = await PlanOfAsync(recording, run.Id);
        recorded.Pools.Should().NotBeEmpty();

        // A Simulator of its own whose model is gone: any call to the agent is counted, and the test expects none.
        var calls = 0;
        await using var withoutModel = await fixture.StartSimulatorWithAgentsAsync(
            services => services.AddSingleton<IAgentClient>(new FailingAgentClient(() => Interlocked.Increment(ref calls))), Ct);
        var replayed = await PlanOfAsync(withoutModel, run.Id);

        replayed.Pools.Should().BeEquivalentTo(recorded.Pools, "the plan keeps its pools in the Simulator's database");
        replayed.CarrierProfiles.Should().BeEquivalentTo(recorded.CarrierProfiles);
        calls.Should().Be(0, "reading a recorded plan asks no model");

        // What the run sent is what the recorded pools draw for the same seed and steps.
        foreach (var reference in References(log))
        {
            var pool = recorded.Pools.Single(pool => pool.Name == reference.Pool);
            var draw = replayed.Pools.Single(candidate => candidate.Name == reference.Pool).Texts[reference.Index];
            draw.Should().Be(pool.Texts[reference.Index]);
        }
    }

    [Fact]
    public async Task ADeclineWithReasonOther_ReachesThePartnerApi_WithATextFromThePool()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 9, Runs.NextSeed(), Declines);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);

        var plan = await PlanOfAsync(simulator, run.Id);
        var declined = log.Where(line => line.Outcome == LogOutcome.Declined).ToList();
        declined.Should().HaveCount(9);
        foreach (var line in declined)
        {
            var booking = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!["carrierBookings"]!.AsArray().Single()!;
            booking["status"]!.GetValue<string>().Should().Be("Rejected");
            booking["reasonCode"]!.GetValue<string>().Should().Be("Other");
            var reference = Reference(line);
            var pool = plan.Pools.Single(pool => pool.Name == reference.Pool);
            pool.Category.Should().Be(TextCategory.DeclineReason);
            booking["reasonText"]!.GetValue<string>().Should().Be(pool.Texts[reference.Index], "the text is the one the seed drew from the Carrier's pool");
        }
    }

    [Fact]
    public async Task ADeliveryFailedEvent_ReachesTheTms_WithARemarkFromThePool()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 16, Runs.NextSeed(), Exceptions);

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);

        var plan = await PlanOfAsync(simulator, run.Id);
        var failed = log.Where(line => line.Outcome == LogOutcome.TrackingSent && line.Detail!.Contains("(DeliveryFailed)", StringComparison.Ordinal)).ToList();
        failed.Should().NotBeEmpty("the Exceptions are dealt in turn");
        foreach (var line in failed)
        {
            var reference = Reference(line);
            var pool = plan.Pools.Single(pool => pool.Name == reference.Pool);
            pool.Category.Should().Be(TextCategory.DeliveryFailedRemark);
            var expected = pool.Texts[reference.Index];
            await Runs.WaitUntilAsync(async () => (await fixture.TimelineAsync(line.ShipmentNumber!, Ct))!["trackingEvents"]!.AsArray()
                .Any(entry => entry!["remark"]?.GetValue<string>() == expected), Ct);
        }
    }

    [Fact]
    public async Task WithTheGeneratorFailing_ThePlanFallsBackToTheBuiltInPools_AndSaysSo()
    {
        await using var simulator = await fixture.StartSimulatorWithAgentsAsync(
            services => services.AddSingleton<IAgentClient>(new FailingAgentClient(() => { })), Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Carried(orders: 9, Runs.NextSeed(), Declines);

        var (preview, run, log) = await simulator.RunToEndAsync(settings, Ct);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        preview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => pool.Source == TextPool.BuiltInSource);
        preview.PoolNotes.Should().NotBeEmpty().And.OnlyContain(note => note.Contains("built-in pool is used", StringComparison.Ordinal));
        simulator.Telemetry.Entries.Should().Contain(entry => entry.Contains("The personas agent gave no pool for", StringComparison.Ordinal));
        var plan = await PlanOfAsync(simulator, run.Id);
        plan.PoolNotes.Should().Equal(preview.PoolNotes);

        foreach (var line in log.Where(line => line.Outcome == LogOutcome.Declined))
        {
            var booking = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!["carrierBookings"]!.AsArray().Single()!;
            var reference = Reference(line);
            booking["reasonText"]!.GetValue<string>().Should().Be(plan.Pools.Single(pool => pool.Name == reference.Pool).Texts[reference.Index]);
        }
    }

    [Fact]
    public async Task NoPlanData_IsInThePrompt_AndNoTextIsInTheLogOrTheTelemetry()
    {
        var requests = new List<PoolRequest>();
        await using var simulator = await fixture.StartSimulatorWithAgentsAsync(
            services =>
            {
                var original = services.Last(service => service.ServiceType == typeof(IAgentClient));
                services.Remove(original);
                services.AddSingleton<IAgentClient>(provider => new RecordingAgentClient((IAgentClient)original.ImplementationFactory!(provider), requests));
            },
            Ct,
            SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Carried(orders: 12, Runs.NextSeed(), Exceptions with { ConfirmPercent = 50, DeclinePercent = 50 });

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);

        var plan = await PlanOfAsync(simulator, run.Id);
        requests.Should().NotBeEmpty();
        requests.Should().OnlyContain(request => request.Count == 8);
        // The prompt is the profile, the language and the kind of text; nothing of the plan can be in it.
        var prompt = string.Join('\n', requests.Select(request => $"{request.Profile.Id} {request.Profile.Style} {request.Profile.Language} {request.Category} {request.Count}"));
        foreach (var booking in plan.Bookings)
        {
            prompt.Should().NotContain(booking.ShipperName).And.NotContain(booking.PersonaName);
        }

        var texts = plan.Pools.SelectMany(pool => pool.Texts).Distinct().ToList();
        texts.Should().NotBeEmpty();
        var seen = log.Select(line => $"{line.Action} {line.Detail}").Concat(simulator.Telemetry.Entries).ToList();
        foreach (var text in texts)
        {
            seen.Should().NotContain(entry => entry.Contains(text, StringComparison.Ordinal), "the log and the telemetry name a pool and an index, never a text");
        }
    }

    private static List<PoolRef> References(IReadOnlyList<LogLine> log) =>
        [.. log.Where(line => line.Detail is not null && PoolReference().IsMatch(line.Detail)).OrderBy(line => line.Sequence).Select(Reference)];

    private static PoolRef Reference(LogLine line)
    {
        var match = PoolReference().Match(line.Detail!);
        match.Success.Should().BeTrue($"the line {line.Sequence} names the pool its text was drawn from");
        return new PoolRef(match.Groups["pool"].Value.TrimEnd(','), int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<SimulationRun> RunAsync(SimulatorHost simulator, Guid runId)
    {
        await using var scope = simulator.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IReadRepository<SimulationRun>>().GetByIdAsync(new SimulationRunId(runId), Ct))!;
    }

    private static async Task<Norvia.Simulator.Domain.Plans.SimulationPlan> PlanOfAsync(SimulatorHost simulator, Guid runId) => (await RunAsync(simulator, runId)).Plan;

    private async Task<SimulatorHost> PricedSimulatorAsync() => await fixture.StartSimulatorWithAgentsAsync(configureServices: null, Ct, SeedEdits.OnlyShippers(PricedShipper));

    private sealed record PoolRef(string Pool, int Index);

    private sealed class FailingAgentClient(Action called) : IAgentClient
    {
        public Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken)
        {
            called();
            return Task.FromResult(Result<IReadOnlyList<string>>.Unavailable("agent-unavailable"));
        }
    }

    private sealed class RecordingAgentClient(IAgentClient inner, List<PoolRequest> requests) : IAgentClient
    {
        public Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken)
        {
            lock (requests)
            {
                requests.Add(request);
            }

            return inner.GeneratePoolAsync(request, cancellationToken);
        }
    }
}
