extern alias SimulatorWeb;

using Ardalis.Result;
using Azure.Messaging.ServiceBus;
using Norvia.Simulator.Application.Reset;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Runs;

using SimulatorWeb::Norvia.Simulator.Web;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The Simulator's Reset estate against the real applications (ADR 0039): the TMS API, the Partner portal and the Driver app API each reset through their own
/// door, and the Simulator clears its own runs and log last. A reset empties the whole estate, so these tests take their turn with the others and each starts from
/// whatever an earlier test left, which the first reset clears.
/// </summary>
[Collection("Simulator runs")]
public sealed class ResetTests(SimulatorFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetAfterARun_LeavesAnEmptyEstateInAllFourApps_AndTheNextRunIsNumberOneWithReferencesFromSIM11()
    {
        var carried = Runs.Carried(orders: 6, Runs.NextSeed(), new CarrierShares(ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 0));
        var (_, carriedRun, _) = await fixture.Simulator.RunToEndAsync(carried, Ct);
        await using var ownFleet = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers("SHP-0001"), SeedEdits.OwnFleetOf("VEH-0003", "DRV-0003"));
        var tripped = Runs.Tripped(orders: 4, Runs.NextSeed(), collectedTodayPercent: 0, new Norvia.Simulator.Domain.Drivers.DriverShares(0, 0, 0, 0));
        var (_, trippedRun, _) = await ownFleet.RunToEndAsync(tripped, Ct, Runs.TripPatience);
        carriedRun.Status.Should().Be(SimulationRunStatus.Completed);
        trippedRun.Status.Should().Be(SimulationRunStatus.Completed);
        var references = await fixture.ShipperReferencesAsync("Planned", Ct);
        references.Should().NotBeEmpty("the runs left Transport Orders in the TMS");
        // A dispatched Trip reaches the Driver app through the TMS's outbox and the Trip dispatch worker, a moment after the run.
        await Runs.WaitUntilAsync(async () => (await fixture.DriverTripsAsync("DRV-0003", Ct)).Count > 0, Ct);

        var report = await ResetWhenQuietAsync(fixture.Simulator);

        report.Complete.Should().BeTrue();
        report.Apps.Select(app => app.App).Should().Equal(EstateApps.All);
        Deleted(report, EstateApp.Tms, "Transport Orders").Should().BePositive();
        Deleted(report, EstateApp.Tms, "Shipments").Should().BePositive();
        Deleted(report, EstateApp.Tms, "Trips").Should().BePositive();
        Deleted(report, EstateApp.PartnerPortal, "Carrier booking requests").Should().BePositive();
        Deleted(report, EstateApp.DriverApp, "Trips").Should().BePositive();
        report.Simulator.Single(kind => kind.Kind == "Runs").Count.Should().BeGreaterThanOrEqualTo(2);

        foreach (var status in new[] { "Accepted", "Planned", "Rejected" })
        {
            (await fixture.ShipperReferencesAsync(status, Ct)).Should().BeEmpty($"the TMS holds no {status} Transport Order after the reset");
        }

        (await fixture.OpenExceptionsAsync(Ct)).Should().BeEmpty();
        (await fixture.Simulator.Requests.ListRunsAsync(Ct)).Should().BeEmpty();
        var again = await ResetWhenQuietAsync(fixture.Simulator);
        again.Apps.SelectMany(app => app.Deleted).Should().OnlyContain(kind => kind.Count == 0, "the Partner portal and the Driver app were emptied as well");

        var next = Runs.Burst(orders: 3, badSharePercent: 0, Runs.NextSeed());
        var (_, run, log) = await fixture.Simulator.RunToEndAsync(next, Ct);
        run.Number.Should().Be(1);
        log.Where(Runs.IsBooking).Select(line => line.ShipperReference).Should().Contain("SIM-1-1");
    }

    [Fact]
    public async Task AResetOnAnEmptyEstate_Succeeds_WithZeroCounts()
    {
        await ResetWhenQuietAsync(fixture.Simulator);

        var report = await ResetWhenQuietAsync(fixture.Simulator);

        report.Complete.Should().BeTrue();
        report.Apps.SelectMany(app => app.Deleted).Should().OnlyContain(kind => kind.Count == 0);
        report.Simulator.Should().OnlyContain(kind => kind.Count == 0);
    }

    [Fact]
    public async Task ATransportOrderNobodyBookedInARun_GoesAsWell()
    {
        var number = await fixture.EnterTransportOrderAsync($"HAND-{Guid.NewGuid():N}"[..20], Ct);
        (await fixture.TransportOrderAsync(number, Ct)).Should().NotBeNull();

        await ResetWhenQuietAsync(fixture.Simulator);

        (await fixture.TransportOrderAsync(number, Ct)).Should().BeNull("a reset empties the TMS, not only what a run made");
    }

    [Fact]
    public async Task AResetWhileARunIsActive_IsRefused_AndNoApplicationIsToldToReset()
    {
        var number = await fixture.EnterTransportOrderAsync($"HAND-{Guid.NewGuid():N}"[..20], Ct);
        using var tms = ResetStub.Accepting();
        await using var simulator = fixture.StartSimulatorWithTmsAt(tms.Address);
        var run = await simulator.Requests.StartAsync(Runs.Paced(orders: 20, Runs.NextSeed()), Ct);

        var refused = await simulator.Requests.ResetEstateAsync(Ct);
        await simulator.Requests.CancelAsync(new SimulationRunId(run.Id), Ct);
        await simulator.Requests.FinishedAsync(run.Id, Ct);

        refused.Status.Should().Be(ResultStatus.Conflict);
        refused.Errors.Should().ContainSingle().Which.Should().Contain("A run is active");
        tms.Calls.Should().NotContain("POST", "no application is told to reset while a run is active");
        (await fixture.TransportOrderAsync(number, Ct)).Should().NotBeNull("nothing was deleted");
    }

    [Fact]
    public async Task AResetWhileASubscriptionHoldsUnprocessedMessages_IsRefusedNamingIt_AndNothingIsDeleted()
    {
        await ResetWhenQuietAsync(fixture.Simulator);
        var number = await fixture.EnterTransportOrderAsync($"HAND-{Guid.NewGuid():N}"[..20], Ct);
        var connectionString = await fixture.ServiceBusConnectionStringAsync(Ct);
        await fixture.StopAsync("shipper-notification-worker", Ct);
        try
        {
            await using var client = new ServiceBusClient(connectionString);
            await using var sender = client.CreateSender("shipment-events");
            await sender.SendMessageAsync(
                new ServiceBusMessage("{}") { ApplicationProperties = { ["eventType"] = "ShipperNotificationRequested" } }, Ct);

            // The emulator takes a moment to put the message on the subscription.
            await Runs.WaitUntilAsync(async () => (await fixture.Simulator.Requests.GetResetReadinessAsync(Ct)).Reason?.Contains("unprocessed", StringComparison.Ordinal) == true, Ct);
            var refused = await fixture.Simulator.Requests.ResetEstateAsync(Ct);

            refused.Status.Should().Be(ResultStatus.Conflict);
            refused.Errors.Should().ContainSingle().Which.Should().Contain("shipment-events/shipper-notification").And.Contain("unprocessed");
            (await fixture.TransportOrderAsync(number, Ct)).Should().NotBeNull("nothing was deleted");

            // The worker is stopped, so the message is taken off the subscription here, which is all the worker would have done with it.
            await using var receiver = client.CreateReceiver("shipment-events", "shipper-notification", new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });
            (await receiver.ReceiveMessagesAsync(maxMessages: 10, TimeSpan.FromSeconds(5), Ct)).Should().NotBeEmpty();
        }
        finally
        {
            await fixture.StartAsync("shipper-notification-worker", CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenOneApplicationRefuses_TheResultNamesItAndWhy_TheOthersCountsAreReported_TheHistoryIsKept_AndAnotherResetCompletes()
    {
        await ResetWhenQuietAsync(fixture.Simulator);
        var (_, run, _) = await fixture.Simulator.RunToEndAsync(Runs.Burst(orders: 4, badSharePercent: 0, Runs.NextSeed()), Ct);
        using var tms = ResetStub.Refusing();
        await using var simulator = fixture.StartSimulatorWithTmsAt(tms.Address);

        var report = (await simulator.Requests.ResetEstateAsync(Ct)).Value;

        report.Complete.Should().BeFalse();
        var tmsResult = report.Apps.Single(app => app.App == EstateApp.Tms);
        tmsResult.Succeeded.Should().BeFalse();
        tmsResult.Refusal.Should().Contain(ResetStub.RefusalDetail);
        report.Apps.Where(app => app.App != EstateApp.Tms).Should().OnlyContain(app => app.Succeeded && app.Deleted.Count > 0, "the other applications were told to reset all the same, and their counts are reported");
        report.Simulator.Should().BeEmpty("the Simulator clears its own history only once every application has reset");
        (await simulator.Requests.ListRunsAsync(Ct)).Should().Contain(summary => summary.Id == run.Id);

        var again = await ResetWhenQuietAsync(fixture.Simulator);

        again.Complete.Should().BeTrue();
        (await fixture.Simulator.Requests.ListRunsAsync(Ct)).Should().BeEmpty();
    }

    private static int Deleted(EstateResetReport report, EstateApp app, string kind) =>
        report.Apps.Single(result => result.App == app).Deleted.Single(deleted => deleted.Kind == kind).Count;

    /// <summary>Resets the estate once the Service Bus subscriptions the applications wait on are empty, which the workers take a moment to bring about after a run.</summary>
    private static async Task<EstateResetReport> ResetWhenQuietAsync(SimulatorHost simulator)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(Patience);
        while (true)
        {
            var result = await simulator.Requests.ResetEstateAsync(timeout.Token);
            if (result.IsSuccess)
            {
                return result.Value;
            }

            result.Status.Should().Be(ResultStatus.Conflict, string.Join("; ", result.Errors));
            result.Errors.Should().ContainSingle().Which.Should().Contain("unprocessed", "only a subscription that is not yet empty is worth waiting for");
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }
    }
}
