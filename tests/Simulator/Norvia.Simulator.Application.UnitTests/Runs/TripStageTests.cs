using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>The Planner after the assignments: the Shipments that stayed with the Own fleet are released and put on Trips within the rules, and each Trip is dispatched.</summary>
public sealed class TripStageTests
{
    private readonly FleetRun _fleet = new();

    [Fact]
    public async Task Handle_EveryOwnFleetShipment_IsReleased_AndPutOnATrip()
    {
        var run = await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Released.Should().HaveCount(FleetRun.Orders);
        _fleet.OnTrips.Should().BeEquivalentTo(_fleet.Released);
        _fleet.Released.Should().Equal(run.Plan.Choices.OrderBy(choice => choice.Step).Select(choice => $"SH-{choice.Step:0000000}"));
    }

    [Fact]
    public async Task Handle_TheTrips_StayWithinTheVehiclesCapacity_AndEachVehicleAndDriverHasOneTripADay()
    {
        await _fleet.RunAsync(FleetRun.Settings(orders: 12));

        _fleet.Created.Should().NotBeEmpty();
        foreach (var (draft, _, _) in _fleet.Created)
        {
            draft.Shipments.Sum(shipment => shipment.Load.WeightKg).Should().BeLessThanOrEqualTo(draft.Vehicle.PayloadKg);
            draft.Shipments.Sum(shipment => shipment.Load.PalletPlaces).Should().BeLessThanOrEqualTo(draft.Vehicle.PalletPlaces);
            draft.Driver.CanDrive(draft.Vehicle).Should().BeTrue();
            draft.PlannedStart.Should().Be(new TimeOnly(6, 0), "the Driver's Shift starts then");
        }

        _fleet.Created.Select(created => (created.Draft.Day, created.Draft.Vehicle.Id)).Should().OnlyHaveUniqueItems();
        _fleet.Created.Select(created => (created.Draft.Day, created.Draft.Driver.Id)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_EachTrip_IsCreatedUnderAKeyOfItsOwn_ThenDispatched()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Created.Select(created => created.Key).Should().OnlyHaveUniqueItems().And.OnlyContain(key => key.StartsWith("SIM-1-", StringComparison.Ordinal) && key.EndsWith("-trip", StringComparison.Ordinal));
        _fleet.Dispatched.Should().Equal(_fleet.Created.Select(created => created.Number));
    }

    [Fact]
    public async Task Handle_ATrip_IsLoggedWithItsNumber_AndADispatchWithItsCorrelationId()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        var number = _fleet.Created.First().Number;
        var created = _fleet.Lines.Single(line => line.Action == "Create Trip" && line.TripNumber == number);
        created.Outcome.Should().Be(LogOutcome.TripBuilt);
        created.Actor.Should().Be(LogEntry.PlannerActor);
        created.Detail.Should().Contain("Shipment").And.Contain("start 06:00");
        var dispatched = _fleet.Lines.Single(line => line.Action == "Dispatch Trip" && line.TripNumber == number);
        dispatched.Outcome.Should().Be(LogOutcome.TripDispatched);
        dispatched.CorrelationId.Should().Be($"disp-{number}");
    }

    [Fact]
    public async Task Handle_TheReleaseOfOwnFleetShipments_IsLoggedLikeTheCarriersOnes()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Lines.Where(line => line.Outcome == LogOutcome.Released).Should().HaveCount(FleetRun.Orders).And.OnlyContain(line => line.Action == "Release Shipment" && line.Detail!.StartsWith("Own fleet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_AShipmentTheTmsWillNotRelease_IsNotPutOnATrip()
    {
        _fleet.Tms.ReleaseAsync("SH-0000001", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlannerAnswer(LogOutcome.PlanningRefused, "ref-1", "SH-0000001", "ShipmentNotPlanned (409)", "ShipmentNotPlanned"));

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.OnTrips.Should().NotContain("SH-0000001").And.HaveCount(FleetRun.Orders - 1);
        _fleet.Lines.Should().Contain(line => line.Outcome == LogOutcome.PlanningRefused && line.ShipmentNumber == "SH-0000001");
    }

    [Fact]
    public async Task Handle_ATripTheTmsRefuses_IsLoggedWithItsCode_AndIsNotDispatched_AndTheRunGoesOn()
    {
        _fleet.Tms.CreateTripAsync(Arg.Any<Norvia.Simulator.Domain.Trips.TripDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlannerAnswer(LogOutcome.PlanningRefused, "ref-trip", null, "DrivingHoursExceeded (409)", "DrivingHoursExceeded"));

        var run = await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Dispatched.Should().BeEmpty();
        var refusal = _fleet.Lines.First(line => line.Action == "Create Trip");
        refusal.Outcome.Should().Be(LogOutcome.PlanningRefused);
        refusal.Detail.Should().Contain("DrivingHoursExceeded");
        refusal.TripNumber.Should().BeNull();
        _fleet.Lines.Last().Action.Should().StartWith($"Run {run.Number} finished");
        await _fleet.Drivers.DidNotReceive().StartTripAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ATripThatCouldNotBeDispatched_IsNotWorkedByItsDriver()
    {
        _fleet.Tms.DispatchTripAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PlannerAnswer(LogOutcome.PlanningRefused, "ref-dispatch", null, "DriverNotAvailable (409)", "DriverNotAvailable"));

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Lines.Should().Contain(line => line.Action == "Dispatch Trip" && line.Outcome == LogOutcome.PlanningRefused && line.TripNumber == _fleet.Created.First().Number);
        _fleet.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoDriverWorksTheDay_NoTripIsBuilt_AndEachShipmentIsNoted()
    {
        _fleet.Days = () => FleetRun.Week();

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Created.Should().BeEmpty();
        _fleet.Lines.Where(line => line.Outcome == LogOutcome.Info && line.Action.Contains("found no Vehicle and Driver left", StringComparison.Ordinal)).Should().HaveCount(FleetRun.Orders);
    }

    [Fact]
    public async Task Handle_MoreShipmentsThanDriversCanCarry_TheRestStayReleased_AndAreNoted()
    {
        _fleet.Days = () => FleetRun.Week(TestData.Sanne);

        await _fleet.RunAsync(FleetRun.Settings(orders: 30));

        _fleet.Created.Should().ContainSingle();
        _fleet.Lines.Count(line => line.Action.Contains("found no Vehicle and Driver left", StringComparison.Ordinal)).Should().Be(30 - _fleet.OnTrips.Count());
    }

    [Fact]
    public async Task Handle_TheTmsCannotSayTheShifts_NoTripIsBuilt_AndTheLogSaysSo()
    {
        // The Planner's assignments read the capacity first; the Trips need the Shifts after them.
        var asked = 0;
        _fleet.Tms.GetOwnFleetDaysAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<FleetDay>?>(++asked == 1 ? FleetRun.Week(TestData.Anouk) : null));

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Created.Should().BeEmpty();
        _fleet.Lines.Should().Contain(line => line.Action.StartsWith("The TMS couldn't tell the Own fleet's Shifts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_ShipmentsForLaterDays_GetTheirTripsOnTheirOwnDays_AndStopAtDispatched()
    {
        await _fleet.RunAsync(FleetRun.Settings(todayPercent: 0));

        _fleet.Created.Should().NotBeEmpty().And.OnlyContain(created => created.Draft.Day > FleetRun.Today);
        _fleet.Dispatched.Should().HaveCount(_fleet.Created.Count);
        _fleet.Calls.Should().BeEmpty();
        await _fleet.Drivers.DidNotReceive().GetTripsAsync(Arg.Any<SimulatedDriver>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoShipmentStaysWithTheOwnFleet_NothingIsReleasedOrBuilt()
    {
        await _fleet.RunAsync(FleetRun.Settings() with { OwnFleetPercent = 0 });

        _fleet.Created.Should().BeEmpty();
        await _fleet.Tms.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ATripShare_PutsExactlyThatShareOfTheOwnFleetShipmentsOnTrips_AndLeavesTheRestPlanned()
    {
        await _fleet.RunAsync(FleetRun.Settings() with { TripPercent = 50 });

        _fleet.Released.Should().HaveCount(FleetRun.Orders / 2);
        _fleet.OnTrips.Should().BeEquivalentTo(_fleet.Released);
    }

    [Fact]
    public async Task Handle_NoTripShare_ReleasesAndBuildsNothing_AndTheDriversHaveNothingToDo()
    {
        await _fleet.RunAsync(FleetRun.Settings() with { TripPercent = 0 });

        _fleet.Released.Should().BeEmpty();
        _fleet.Created.Should().BeEmpty();
        _fleet.Calls.Should().BeEmpty();
        _fleet.Lines.Last().Action.Should().NotContain("Trips dispatched");
    }

    [Fact]
    public async Task Handle_Finished_CountsTheTripsDispatchedAndWorked()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Lines.Last().Action.Should().Contain($"{_fleet.Created.Count} Trips dispatched, {_fleet.Created.Count} started and {_fleet.Created.Count} completed by their Drivers");
    }

    [Fact]
    public async Task Handle_TheRunIsCancelledWhileTripsAreBuilt_NoMoreTripsAreMade()
    {
        using var stop = new CancellationTokenSource();
        _fleet.Tms.CreateTripAsync(Arg.Any<Norvia.Simulator.Domain.Trips.TripDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            stop.Cancel();
            return new PlannerAnswer(LogOutcome.TripBuilt, null, null, null, TripNumber: "TR-0000001");
        });

        await _fleet.RunAsync(FleetRun.Settings(orders: 30), stop);

        _fleet.Lines.Should().Contain(line => line.Action == "Run cancelled: nothing more is sent");
        await _fleet.Tms.Received(1).CreateTripAsync(Arg.Any<Norvia.Simulator.Domain.Trips.TripDraft>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
