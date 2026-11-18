extern alias SimulatorWeb;

using System.Text.RegularExpressions;
using Ardalis.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The opt-in hostile profile (#487) at the Simulator's boundary, with the Agents on: the personas agent is the in-process runner with the scripted fake model, and
/// the Partner API and the TMS are the real ones. A hostile run is selected on purpose, per Carrier; it is labelled <c>profile: hostile</c> in the plan, the run log
/// and the tracking keys, and its remarks reach the TMS as ordinary remarks.
/// </summary>
/// <remarks>
/// The Own fleet's Drivers are covered at the plan and the log only: the TMS gives each Vehicle and Driver one Trip a day and every Vehicle of the seed belongs to
/// another test class, so a hostile Driver's Trip would collide with theirs.
/// </remarks>
[Collection("Simulator runs")]
public sealed partial class HostileProfileTests(SimulatorFixture fixture)
{
    // The Shipper whose every Lane the Carriers' Rate Cards cover, so the TMS can price each release; and the Carrier chosen for the hostile profile.
    private const string PricedShipper = "SHP-0001";
    private const string HostileCarrier = "CAR-0001";
    private const string HostileDriver = "DRV-0002";

    private static readonly CarrierShares Declines = new(
        ReleasePercent: 100, ConfirmPercent: 0, DeclinePercent: 100, TrackedPercent: 0, ExceptionPercent: 0,
        DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);

    // Every Shipment is tracked and hits an Exception; the Exceptions are dealt in turn, so some are failed deliveries.
    private static readonly CarrierShares Exceptions = new(
        ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 100,
        DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"profile: hostile, pool (?<pool>\S+), index (?<index>\d+)")]
    private static partial Regex HostileReference();

    [Fact]
    public async Task ARunThatChoosesNoHostileCarrierOrDriver_IsNotHostile_AndLabelsNothing()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 9, Runs.NextSeed(), Declines);

        var (preview, run, log) = await simulator.RunToEndAsync(settings, Ct);

        settings.IsHostile.Should().BeFalse("the profile is off by default");
        preview.IsHostile.Should().BeFalse();
        preview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => !pool.IsHostile);
        var plan = await PlanOfAsync(simulator, run.Id);
        plan.IsHostile.Should().BeFalse();
        plan.Pools.Should().OnlyContain(pool => !pool.IsHostile);
        log.Should().OnlyContain(line => !(line.Detail ?? string.Empty).Contains("profile: hostile", StringComparison.Ordinal) && !line.Action.Contains("profile: hostile", StringComparison.Ordinal));
        simulator.Telemetry.Entries.Should().NotContain(entry => entry.Contains("profile=hostile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AHostileCarrier_IsLabelledInThePlan_ThePreviewAndTheFirstLineOfTheLog()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 9, Runs.NextSeed(), Declines) with { HostileCarrierIds = [HostileCarrier] };

        var (preview, run, log) = await simulator.RunToEndAsync(settings, Ct);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        preview.IsHostile.Should().BeTrue();
        preview.HostileCarrierIds.Should().Equal(HostileCarrier);
        var hostilePools = preview.Pools.Where(pool => pool.IsHostile).ToList();
        hostilePools.Should().NotBeEmpty().And.OnlyContain(pool => pool.ProfileId == PersonaProfile.HostileId);
        preview.Pools.Where(pool => !pool.IsHostile).Should().OnlyContain(pool => pool.ProfileId != PersonaProfile.HostileId, "the other Carriers keep their own profiles");

        var plan = await PlanOfAsync(simulator, run.Id);
        plan.IsHostile.Should().BeTrue();
        plan.HostileCarrierIds.Should().Equal(HostileCarrier);
        plan.CarrierProfiles[HostileCarrier].Should().Be(PersonaProfile.HostileId);

        var first = log.OrderBy(line => line.Sequence).First();
        first.Action.Should().Contain("HOSTILE RUN (profile: hostile)").And.Contain(HostileCarrier);
    }

    [Fact]
    public async Task ADeclineFromAHostileCarrier_ReachesThePartnerApi_AsAnOrdinaryReasonOther_AndIsLabelledInTheLogAndTheTrackingKeys()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 9, Runs.NextSeed(), Declines) with { HostileCarrierIds = [HostileCarrier] };

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);

        var plan = await PlanOfAsync(simulator, run.Id);
        var hostile = log.Where(line => line.Outcome == LogOutcome.Declined && HostileReference().IsMatch(line.Detail ?? string.Empty)).ToList();
        hostile.Should().NotBeEmpty("the chosen Carrier declines some of the Shipments");
        foreach (var line in hostile)
        {
            var reference = ReferenceOf(line);
            var pool = plan.Pools.Single(candidate => candidate.Name == reference.Pool);
            pool.IsHostile.Should().BeTrue();
            pool.Category.Should().Be(TextCategory.DeclineReason);
            var expected = pool.Texts[reference.Index];

            // The TMS holds it as any other reason: a rejected booking with the reason Other and the text, with nothing of the profile in it.
            var booking = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!["carrierBookings"]!.AsArray().Single()!;
            booking["status"]!.GetValue<string>().Should().Be("Rejected");
            booking["reasonCode"]!.GetValue<string>().Should().Be("Other");
            booking["reasonText"]!.GetValue<string>().Should().Be(expected);

            // The Simulator's trace of the call carries the label beside its tracking keys, and never the text.
            simulator.Telemetry.Entries.Should().Contain(
                entry => entry.Contains("profile=hostile", StringComparison.Ordinal) && entry.Contains($"correlationId={line.CorrelationId}", StringComparison.Ordinal),
                $"the trace of the decline of {line.ShipmentNumber} is labelled");
        }

        simulator.Telemetry.Entries.Should().NotContain(entry => plan.Pools.Where(pool => pool.IsHostile).SelectMany(pool => pool.Texts).Any(text => entry.Contains(text, StringComparison.Ordinal)),
            "the log and the telemetry name a pool and an index, never a text");
    }

    [Fact]
    public async Task ADeliveryFailedRemarkFromAHostileCarrier_ReachesTheTms_OnTheTimelineAsAnOrdinaryRemark()
    {
        await using var simulator = await PricedSimulatorAsync();

        // Which Carrier the Planner gives the Shipments that fail a delivery is up to the seed, and about one run in five gives the chosen Carrier none. Such a
        // run has nothing to check, so it is run again with another seed.
        Guid runId;
        List<LogLine> failed;
        var attempt = 0;
        do
        {
            var settings = Runs.Carried(orders: 16, Runs.NextSeed(), Exceptions) with { HostileCarrierIds = [HostileCarrier] };
            var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);
            runId = run.Id;
            failed = [.. log.Where(line => line.Outcome == LogOutcome.TrackingSent
                && (line.Detail ?? string.Empty).Contains("(DeliveryFailed)", StringComparison.Ordinal) && HostileReference().IsMatch(line.Detail!))];
        }
        while (failed.Count == 0 && ++attempt < 10);

        var plan = await PlanOfAsync(simulator, runId);
        failed.Should().NotBeEmpty("the Exceptions are dealt in turn and the chosen Carrier is one of those that fail a delivery");
        foreach (var line in failed)
        {
            var reference = ReferenceOf(line);
            var pool = plan.Pools.Single(candidate => candidate.Name == reference.Pool);
            pool.IsHostile.Should().BeTrue();
            pool.Category.Should().Be(TextCategory.DeliveryFailedRemark);
            var expected = pool.Texts[reference.Index];
            await Runs.WaitUntilAsync(async () => (await fixture.TimelineAsync(line.ShipmentNumber!, Ct))!["trackingEvents"]!.AsArray()
                .Any(entry => entry!["remark"]?.GetValue<string>() == expected), Ct);

            simulator.Telemetry.Entries.Should().Contain(
                entry => entry.Contains("profile=hostile", StringComparison.Ordinal) && entry.Contains($"correlationId={line.CorrelationId}", StringComparison.Ordinal),
                $"the trace of the tracking event of {line.ShipmentNumber} is labelled");
        }
    }

    [Fact]
    public async Task AHostileRun_LeavesTheCarriersThatWereNotChosenWithTheirOwnWords()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 12, Runs.NextSeed(), Declines) with { HostileCarrierIds = [HostileCarrier] };

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct);

        var plan = await PlanOfAsync(simulator, run.Id);
        var others = log.Where(line => line.Outcome == LogOutcome.Declined && !HostileReference().IsMatch(line.Detail ?? string.Empty)).ToList();
        foreach (var line in others)
        {
            var booking = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!["carrierBookings"]!.AsArray().Single()!;
            var text = booking["reasonText"]?.GetValue<string>();
            plan.Pools.Where(pool => pool.IsHostile).SelectMany(pool => pool.Texts).Should().NotContain(text!, "a Carrier that wasn't chosen sends no hostile text");
        }

        var hostileCalls = log.Count(line => line.Outcome == LogOutcome.Declined && HostileReference().IsMatch(line.Detail ?? string.Empty));
        hostileCalls.Should().BeGreaterThan(0);
        simulator.Telemetry.Entries.Count(entry => entry.Contains("profile=hostile", StringComparison.Ordinal))
            .Should().Be(hostileCalls, "only the hostile calls are labelled");
    }

    [Fact]
    public async Task AHostileDriver_IsLabelledInThePlan_ThePreviewAndTheFirstLineOfTheLog()
    {
        // Drivers are the Own fleet's: DRV-0002 and its Vehicle are chosen as in the Driver texts, but the run plans no Trips (the TMS gives each of them one a day).
        await using var simulator = await fixture.StartSimulatorWithAgentsAsync(
            configureServices: null, Ct, SeedEdits.OnlyShippers(PricedShipper), SeedEdits.OwnFleetOf("VEH-0002", HostileDriver));
        var settings = Runs.Carried(orders: 3, Runs.NextSeed(), Declines) with { HostileDriverIds = [HostileDriver] };

        var preview = (await simulator.Requests.PreviewAsync(settings, Ct)).Value;

        preview.IsHostile.Should().BeTrue();
        preview.HostileDriverIds.Should().Equal(HostileDriver);
        var pools = preview.Pools.Where(pool => pool.IsHostile && pool.IsDriverText).ToList();
        pools.Select(pool => pool.Category).Should().BeEquivalentTo(TextCategories.ForDrivers.Select(category => category.ToString()));
        pools.Should().OnlyContain(pool => pool.DriverIds!.Contains(HostileDriver) && pool.ProfileId == PersonaProfile.HostileId);
    }

    [Fact]
    public async Task AChoiceOfSomeoneWhoCannotBeHostile_IsSaidInANote_AndWritesNothingHostile()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 3, Runs.NextSeed(), Declines) with { HostileCarrierIds = ["CAR-9999"] };

        var preview = (await simulator.Requests.PreviewAsync(settings, Ct)).Value;

        preview.PoolNotes.Should().Contain(note => note.Contains("CAR-9999", StringComparison.Ordinal) && note.Contains("hostile", StringComparison.Ordinal));
        preview.Pools.Should().OnlyContain(pool => !pool.IsHostile);
    }

    [Fact]
    public async Task ASettingsOfAHostileRun_ReadBackAsTheyWereChosen_SoRunAgainRepeatsThemOnPurpose()
    {
        await using var simulator = await PricedSimulatorAsync();
        var settings = Runs.Carried(orders: 3, Runs.NextSeed(), Declines) with { HostileCarrierIds = [HostileCarrier], HostileDriverIds = [HostileDriver] };

        var (_, run, _) = await simulator.RunToEndAsync(settings, Ct);

        var detail = (await simulator.Requests.GetRunAsync(new SimulationRunId(run.Id), Ct)).Value;
        detail.Settings.Should().Be(settings);
        detail.Settings.HostileCarrierIds.Should().Equal(HostileCarrier);
        detail.Settings.HostileDriverIds.Should().Equal(HostileDriver);
    }

    private static HostileRef ReferenceOf(LogLine line)
    {
        var match = HostileReference().Match(line.Detail!);
        match.Success.Should().BeTrue($"the line {line.Sequence} names the hostile pool its text was drawn from");
        return new HostileRef(match.Groups["pool"].Value.TrimEnd(','), int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<SimulationPlan> PlanOfAsync(SimulatorHost simulator, Guid runId)
    {
        await using var scope = simulator.Services.CreateAsyncScope();
        var run = await scope.ServiceProvider.GetRequiredService<IReadRepository<SimulationRun>>().GetByIdAsync(new SimulationRunId(runId), Ct);
        return run!.Plan;
    }

    private async Task<SimulatorHost> PricedSimulatorAsync() => await fixture.StartSimulatorWithAgentsAsync(configureServices: null, Ct, SeedEdits.OnlyShippers(PricedShipper));

    private sealed record HostileRef(string Pool, int Index);
}
