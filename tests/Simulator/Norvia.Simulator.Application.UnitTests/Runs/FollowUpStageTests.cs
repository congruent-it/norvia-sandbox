using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Trips;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>The Planner after the Drivers: finds the Exceptions their failures opened, and puts the failed deliveries on a Trip of a later day.</summary>
public sealed class FollowUpStageTests
{
    private readonly FleetRun _fleet = new();

    // Built as the Fleet's own stub builds one, so the Driver app shows the Trip.
    private PlannerAnswer Built(TripDraft draft, string key, string number)
    {
        _fleet.Created.Add((draft, key, number));
        return new PlannerAnswer(LogOutcome.TripBuilt, null, null, null, TripNumber: number);
    }

    [Fact]
    public async Task Handle_EachFailureAndDamage_IsFoundAsAnException_OnItsShipment()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 50, DeliveryFailedPercent: 0, DamagedPercent: 100, ReservationsPercent: 0)));

        var found = _fleet.Lines.Where(line => line.Action == "Find Exception").ToList();
        found.Should().HaveCount(FleetRun.Orders + FleetRun.Orders / 2, "damage on every Shipment and a failed collection on half");
        found.Should().OnlyContain(line => line.Outcome == LogOutcome.ExceptionFound && line.Actor == LogEntry.PlannerActor && line.ShipmentNumber != null && line.ShipperReference != null);
        found.Should().Contain(line => line.Detail!.EndsWith("Damaged", StringComparison.Ordinal)).And.Contain(line => line.Detail!.EndsWith("CollectionFailed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_NothingGoingWrong_AsksForNoExceptions()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        await _fleet.Tms.DidNotReceive().GetOpenExceptionsAsync(Arg.Any<CancellationToken>());
        _fleet.Lines.Should().NotContain(line => line.Action == "Find Exception");
    }

    [Fact]
    public async Task Handle_AnExceptionThatDoesNotOpenInTime_IsNoted()
    {
        _fleet.Tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>([]));

        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(DamagedPercent: 100, CollectionFailedPercent: 0, DeliveryFailedPercent: 0, ReservationsPercent: 0)));

        _fleet.Lines.Count(line => line.Action.Contains("Exception of", StringComparison.Ordinal) && line.Action.Contains("didn't open in time", StringComparison.Ordinal)).Should().Be(FleetRun.Orders);
    }

    [Fact]
    public async Task Handle_TheTmsCannotSayTheOpenExceptions_TheLogSaysSo()
    {
        _fleet.Tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>(null));

        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(DamagedPercent: 100, CollectionFailedPercent: 0, DeliveryFailedPercent: 0, ReservationsPercent: 0)));

        _fleet.Lines.Should().Contain(line => line.Action.StartsWith("The TMS couldn't tell the Open Exceptions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_FailedDeliveries_GoOnATripOfALaterDay_WhichIsDispatched()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 0, DeliveryFailedPercent: 100, DamagedPercent: 0, ReservationsPercent: 0)));

        var failed = _fleet.Calls.Where(call => call.Action == DriverActions.DeliveryFailed).Select(call => call.Shipment!).ToList();
        failed.Should().HaveCount(FleetRun.Orders);
        var redelivery = _fleet.Created.Where(created => created.Draft.Day > FleetRun.Today).ToList();
        redelivery.SelectMany(created => created.Draft.Shipments.Select(shipment => shipment.ShipmentNumber)).Should().BeEquivalentTo(failed);
        redelivery.Should().OnlyContain(created => _fleet.Dispatched.Contains(created.Number));
        _fleet.Lines.Last().Action.Should().Contain($"{redelivery.Count} Re-delivery Trips dispatched");
    }

    [Fact]
    public async Task Handle_FailedCollections_AreNotRedelivered()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 100, DeliveryFailedPercent: 0, DamagedPercent: 0, ReservationsPercent: 0)));

        _fleet.Created.Should().OnlyContain(created => created.Draft.Day == FleetRun.Today);
        _fleet.Lines.Last().Action.Should().NotContain("Re-delivery");
    }

    [Fact]
    public async Task Handle_NoLaterDayHasDrivers_TheRedeliveriesAreNoted()
    {
        var asked = 0;
        _fleet.Tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult<IReadOnlyList<FleetDay>?>([.. FleetRun.Week(TestData.Anouk, TestData.Bram, TestData.Sanne).Select((day, index) => ++asked <= 10 || index == 0 ? day : day with { }).Take(1)]));

        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 0, DeliveryFailedPercent: 100, DamagedPercent: 0, ReservationsPercent: 0)));

        _fleet.Lines.Should().Contain(line => line.Action.StartsWith("No later day has Drivers at work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_ARedeliveryTheTmsRefuses_IsLogged_AndNotDispatched()
    {
        var created = 0;
        _fleet.Tms.CreateTripAsync(Arg.Any<TripDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<TripDraft>().Day > FleetRun.Today
                ? new PlannerAnswer(LogOutcome.PlanningRefused, "ref", null, "ShipmentNotReleased (409)", "ShipmentNotReleased")
                : Built(call.Arg<TripDraft>(), call.ArgAt<string>(1), $"TR-{++created:0000000}"));

        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 0, DeliveryFailedPercent: 100, DamagedPercent: 0, ReservationsPercent: 0)));

        _fleet.Lines.Should().Contain(line => line.Action == "Create Trip" && line.Outcome == LogOutcome.PlanningRefused && line.Detail!.Contains("ShipmentNotReleased", StringComparison.Ordinal));
        _fleet.Lines.Last().Action.Should().NotContain("Re-delivery");
    }

    [Fact]
    public async Task Handle_TheRunIsCancelledWhileWaitingForExceptions_NothingMoreIsDone()
    {
        using var stop = new CancellationTokenSource();
        _fleet.Tms.GetOpenExceptionsAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            stop.Cancel();
            return Task.FromResult<IReadOnlyList<OpenExceptionSummary>?>([]);
        });

        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 0, DeliveryFailedPercent: 100, DamagedPercent: 0, ReservationsPercent: 0)), stop);

        _fleet.Created.Should().OnlyContain(created => created.Draft.Day == FleetRun.Today);
        _fleet.Lines.Should().Contain(line => line.Action == "Run cancelled: nothing more is sent");
    }
}
