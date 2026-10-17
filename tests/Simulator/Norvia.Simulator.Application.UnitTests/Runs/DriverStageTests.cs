using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.UnitTests.Runs;

/// <summary>The Drivers of the Trips for today, through the Driver app: start, collect, deliver or fail, report damage, end.</summary>
public sealed class DriverStageTests
{
    private readonly FleetRun _fleet = new();

    private IEnumerable<string> ActionsOn(string trip) => _fleet.Calls.Where(call => call.Trip == trip).Select(call => call.Action);

    [Fact]
    public async Task Handle_TheTripsForToday_AreWorkedFromStartToEnd_InOrder()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        foreach (var (draft, _, number) in _fleet.Created)
        {
            var actions = ActionsOn(number).ToList();
            actions.First().Should().Be(DriverActions.StartTrip);
            actions.Last().Should().Be(DriverActions.EndTrip);
            actions.Count(action => action == DriverActions.Collected).Should().Be(draft.Shipments.Count);
            actions.Count(action => action == DriverActions.Deliver).Should().Be(draft.Shipments.Count);
        }
    }

    [Fact]
    public async Task Handle_EachShipment_IsCollectedBeforeItIsDelivered()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        foreach (var shipment in _fleet.OnTrips)
        {
            var actions = _fleet.Calls.Where(call => call.Shipment == shipment).Select(call => call.Action).ToList();
            actions.Should().Equal(DriverActions.Collected, DriverActions.Deliver);
        }
    }

    [Fact]
    public async Task Handle_EachDriver_WorksTheirOwnTripAsThemselves()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        foreach (var (draft, _, number) in _fleet.Created)
        {
            _fleet.Calls.Where(call => call.Trip == number).Should().OnlyContain(call => call.Driver.Id == draft.Driver.Id);
        }
    }

    [Fact]
    public async Task Handle_TheTripsOfLaterDays_AreLeftDispatched()
    {
        await _fleet.RunAsync(FleetRun.Settings(todayPercent: 0));

        _fleet.Created.Should().NotBeEmpty();
        _fleet.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheReportsAreLogged_WithTheDriverTheTripTheShipmentAndTheCorrelationId()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        var (draft, _, number) = _fleet.Created[0];
        var shipment = draft.Shipments[0].ShipmentNumber;
        var collected = _fleet.Lines.Single(line => line.Action == "Collected" && line.ShipmentNumber == shipment);
        collected.Actor.Should().Be(draft.Driver.Name);
        collected.TripNumber.Should().Be(number);
        collected.Outcome.Should().Be(LogOutcome.DriverReported);
        collected.CorrelationId.Should().Be($"drv-Collected-{number}-{shipment}");
        collected.ShipperReference.Should().StartWith("SIM-1-");
        collected.TransportOrderNumber.Should().StartWith("TO-");
        _fleet.Lines.Single(line => line.Action == "Start Trip" && line.TripNumber == number).ShipmentNumber.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ATripTheTmsRecordedAsCompleted_IsLoggedAsCompleted()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        foreach (var (_, _, number) in _fleet.Created)
        {
            _fleet.Lines.Should().Contain(line => line.TripNumber == number && line.Action == "End Trip" && line.Outcome == LogOutcome.TripCompleted);
        }
    }

    [Fact]
    public async Task Handle_CollectionsThatFail_ReportCollectionFailedWithARemark_AndDeliverNothing()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(CollectionFailedPercent: 100, 0, 0, 0)));

        _fleet.Calls.Should().NotContain(call => call.Action == DriverActions.Collected || call.Action == DriverActions.Deliver);
        _fleet.Calls.Count(call => call.Action == DriverActions.CollectionFailed).Should().Be(FleetRun.Orders);
        await _fleet.Drivers.Received(FleetRun.Orders).FailCollectionAsync(
            Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string>(remark => remark.Length > 0), Arg.Any<CancellationToken>());
        _fleet.Calls.Count(call => call.Action == DriverActions.EndTrip).Should().Be(_fleet.Created.Count);
    }

    [Fact]
    public async Task Handle_DeliveriesThatFail_AreCollectedFirst_ThenReportDeliveryFailedWithARemark()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(0, DeliveryFailedPercent: 100, 0, 0)));

        _fleet.Calls.Count(call => call.Action == DriverActions.Collected).Should().Be(FleetRun.Orders);
        _fleet.Calls.Count(call => call.Action == DriverActions.DeliveryFailed).Should().Be(FleetRun.Orders);
        _fleet.Calls.Should().NotContain(call => call.Action == DriverActions.Deliver);
        await _fleet.Drivers.Received(FleetRun.Orders).FailDeliveryAsync(
            Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string>(remark => remark.Length > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Damage_IsReportedAtTheCollection_BeforeItsOutcome()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(0, 0, DamagedPercent: 100, 0)));

        foreach (var shipment in _fleet.OnTrips)
        {
            _fleet.Calls.Where(call => call.Shipment == shipment).Select(call => call.Action)
                .Should().Equal(DriverActions.ReportDamage, DriverActions.Collected, DriverActions.Deliver);
        }

        await _fleet.Drivers.Received(FleetRun.Orders).ReportDamageAsync(
            Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), false, Arg.Is<string>(remark => remark.Length > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Reservations_AreDeliveredWithARemark_AndCleanDeliveriesWithout()
    {
        await _fleet.RunAsync(FleetRun.Settings(drivers: new DriverShares(0, 0, 0, ReservationsPercent: 50)));

        await _fleet.Drivers.Received(FleetRun.Orders / 2).DeliverAsync(
            Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<DriverDelivery>(delivery => delivery.Reservations && delivery.Remark != null && delivery.Photos.Count == 1), Arg.Any<CancellationToken>());
        await _fleet.Drivers.Received(FleetRun.Orders - (FleetRun.Orders / 2)).DeliverAsync(
            Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<DriverDelivery>(delivery => !delivery.Reservations && delivery.Remark == null && delivery.ReceivedBy.Length > 0 && delivery.Photos.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EveryDelivery_CarriesItsOwnDrawnSignature_AndTheConsigneesContactAsReceiver()
    {
        await _fleet.RunAsync(FleetRun.Settings());

        var deliveries = _fleet.Drivers.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IDriverApp.DeliverAsync))
            .Select(call => (DriverDelivery)call.GetArguments()[3]!).ToList();
        deliveries.Should().HaveCount(FleetRun.Orders);
        deliveries.Select(delivery => Convert.ToBase64String(delivery.Signature.Span)).Distinct().Should().HaveCount(FleetRun.Orders);
        deliveries.Should().OnlyContain(delivery => delivery.Signature.Length > 100 && delivery.ReceivedBy.Length > 0);
    }

    [Fact]
    public async Task Handle_TheSameSeed_GivesTheSameJourneys()
    {
        var shares = new DriverShares(30, 30, 20, 30);
        await _fleet.RunAsync(FleetRun.Settings(drivers: shares, orders: 10));
        var first = _fleet.Calls.Select(call => (call.Action, call.Shipment)).ToList();

        var again = new FleetRun();
        await again.RunAsync(FleetRun.Settings(drivers: shares, orders: 10));

        again.Calls.Select(call => (call.Action, call.Shipment)).Should().Equal(first);
    }

    [Fact]
    public async Task Handle_ATripThatNeverReachesTheDriverApp_IsNotStarted_AndTheLogSaysSo()
    {
        _fleet.Snapshot = _ => null;

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Calls.Should().BeEmpty();
        _fleet.Lines.Count(line => line.Action.Contains("didn't reach the Driver app in time", StringComparison.Ordinal)).Should().Be(_fleet.Created.Count);
    }

    [Fact]
    public async Task Handle_AStartTheDriverAppRefuses_EndsThatTripsWork_ButNotTheOthers()
    {
        var first = "TR-0000001";
        _fleet.Drivers.StartTripAsync(Arg.Any<SimulatedDriver>(), first, Arg.Any<CancellationToken>())
            .Returns(new DriverCallAnswer(LogOutcome.DriverRefused, null, "TripNotFound (404)"));

        await _fleet.RunAsync(FleetRun.Settings(orders: 12));

        _fleet.Created.Count.Should().BeGreaterThan(1);
        ActionsOn(first).Should().BeEmpty();
        _fleet.Lines.Should().Contain(line => line.Action == "Start Trip" && line.TripNumber == first && line.Outcome == LogOutcome.DriverRefused && line.Detail!.Contains("TripNotFound (404)", StringComparison.Ordinal));
        ActionsOn("TR-0000002").Should().Contain(DriverActions.EndTrip);
    }

    [Fact]
    public async Task Handle_ATripWhoseStartTheTmsDoesNotRecord_IsNotWorked()
    {
        _fleet.Snapshot = trip => new DriverTripSnapshot(trip, 1, DriverTripStatus.Dispatched, 0);

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Calls.Should().OnlyContain(call => call.Action == DriverActions.StartTrip);
        _fleet.Lines.Should().Contain(line => line.Action.Contains("didn't record the start of Trip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_AnEndTheTmsDoesNotRecord_IsNotedAsNotCompleted()
    {
        _fleet.Snapshot = trip => new DriverTripSnapshot(trip, 1, DriverTripStatus.InProgress, 0);

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Lines.Should().NotContain(line => line.Outcome == LogOutcome.TripCompleted);
        _fleet.Lines.Should().Contain(line => line.Action.Contains("didn't record the end of Trip", StringComparison.Ordinal));
        _fleet.Lines.Last().Action.Should().Contain("started and 0 completed");
    }

    [Fact]
    public async Task Handle_AReportTheDriverAppRefuses_IsLogged_AndTheDriverGoesOn()
    {
        var shipment = "SH-0000001";
        _fleet.Drivers.CollectAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), shipment, Arg.Any<CancellationToken>())
            .Returns(new DriverCallAnswer(LogOutcome.DriverRefused, null, "StopNotAvailable (409)"));

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Lines.Should().Contain(line => line.ShipmentNumber == shipment && line.Action == "Collected" && line.Outcome == LogOutcome.DriverRefused);
        _fleet.Calls.Where(call => call.Shipment == shipment).Should().BeEmpty("a Shipment that wasn't collected isn't delivered");
        _fleet.Calls.Should().Contain(call => call.Action == DriverActions.EndTrip);
    }

    [Fact]
    public async Task Handle_ADriverAppThatCannotBeReached_IsLoggedAsNotReceived()
    {
        _fleet.Drivers.StartTripAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DriverCallAnswer(LogOutcome.DriverNotReceived, null, "The call didn't reach the Driver app API."));

        await _fleet.RunAsync(FleetRun.Settings());

        _fleet.Lines.Should().Contain(line => line.Action == "Start Trip" && line.Outcome == LogOutcome.DriverNotReceived);
        _fleet.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheRunIsCancelledBetweenTrips_NoMoreTripsAreWorked()
    {
        using var stop = new CancellationTokenSource();
        _fleet.Drivers.EndTripAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            stop.Cancel();
            return new DriverCallAnswer(LogOutcome.DriverReported, "end", null);
        });

        await _fleet.RunAsync(FleetRun.Settings(orders: 30), stop);

        await _fleet.Drivers.Received(1).EndTripAsync(Arg.Any<SimulatedDriver>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _fleet.Lines.Should().Contain(line => line.Action == "Run cancelled: nothing more is sent");
    }

    [Fact]
    public async Task Handle_TheRunIsCancelledWhileADriverWaitsForTheTrip_TheDriverStops()
    {
        using var stop = new CancellationTokenSource();
        _fleet.Snapshot = _ =>
        {
            stop.Cancel();
            return null;
        };

        await _fleet.RunAsync(FleetRun.Settings(), stop);

        _fleet.Calls.Should().BeEmpty();
        _fleet.Lines.Should().Contain(line => line.Action == "Run cancelled: nothing more is sent");
    }
}
