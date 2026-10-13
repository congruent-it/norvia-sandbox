using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.GetForDriver;
using Norvia.DriverApp.Application.Trips.GetStop;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>
/// What the Driver sees of a Trip's Stops (P12): in the Planner's sequence, each with its outcome and report state, and whether it is available. A new Trip
/// version keeps the outcomes and report states of Stops already completed, and a completed Stop whose Shipment was taken off is kept and marked.
/// </summary>
public sealed class StopProgressTests
{
    private readonly ReportsWorld _world = new();

    public StopProgressTests() => _world.GiveTodaysTripStops("SH-0000001", "SH-0000002");

    private async Task<DriverTripView> Trip() =>
        (await new GetDriverTripsHandler(_world.TripRepository, _world.ReadReportRepository, _world.Time).Handle(new GetDriverTripsQuery("DRV-0002"), CancellationToken.None))
            .Single(trip => trip.TripNumber == "TR-0000012");

    private DriverReport Report(ReportKind kind, StopKind stop, string shipment, ReportState state = ReportState.Recorded, int minutes = 1)
    {
        var at = _world.Time.GetUtcNow().AddMinutes(minutes);
        var report = DriverReport.ReportStop(new ReportId(Guid.NewGuid()), kind, ReportsWorld.TripToday, shipment, stop, ReportsWorld.Joost, 3, at, "corr", kind == ReportKind.Collected ? null : "A remark");
        _ = state switch
        {
            ReportState.Recorded => report.Record(at),
            ReportState.Refused => report.Refuse("ShipmentNotOnTrip", "SH-0000002 is no longer on TR-0000012.", at),
            ReportState.NotRecorded => report.GiveUp(null, "gave up", at),
            _ => false,
        };
        _world.Reports.Add(report);
        return report;
    }

    [Fact]
    public async Task TheStops_AreInThePlannersSequence_EachOpenAndBlockedUntilTheTripIsStarted()
    {
        var trip = await Trip();

        trip.Stops.Select(stop => (stop.ShipmentNumber, stop.Kind)).Should().Equal(
            ("SH-0000001", StopKindDto.Collection), ("SH-0000002", StopKindDto.Collection), ("SH-0000001", StopKindDto.Delivery), ("SH-0000002", StopKindDto.Delivery));
        trip.Stops.Should().OnlyContain(stop => stop.Outcome == null && !stop.CanComplete && !stop.CanReportDamage && stop.Blocked == "Start the Trip first.");
        trip.Stops[0].Details!.Address!.City.Should().Be("Rotterdam");
    }

    [Fact]
    public async Task OnceTheTripIsInProgress_EveryCollectionIsAvailable_AndADeliveryWaitsForItsCollection()
    {
        _world.StartTodaysTrip();

        var stops = (await Trip()).Stops;

        stops.Where(stop => stop.Kind == StopKindDto.Collection).Should().OnlyContain(stop => stop.CanComplete && stop.Blocked == null);
        stops.Where(stop => stop.Kind == StopKindDto.Delivery).Should().OnlyContain(stop => !stop.CanComplete && stop.Blocked == "Collect the Shipment first.");
        stops.Should().OnlyContain(stop => stop.CanReportDamage, "damage can be reported on any Stop");
    }

    [Fact]
    public async Task ACollectedShipment_MakesItsDeliveryAvailable_AndShowsItsOutcomeWithTheReportState()
    {
        _world.StartTodaysTrip();
        var collected = Report(ReportKind.Collected, StopKind.Collection, "SH-0000001", ReportState.Sending);

        var stops = (await Trip()).Stops;

        var collection = stops.Single(stop => stop is { ShipmentNumber: "SH-0000001", Kind: StopKindDto.Collection });
        collection.Completed.Should().BeTrue();
        collection.CanComplete.Should().BeFalse();
        collection.Outcome.Should().Be(new TripReportView(collected.Id.Value, ReportKindDto.Collected, ReportStateDto.Sending, null, null, collected.EventTime, "SH-0000001", StopKindDto.Collection, null));
        stops.Single(stop => stop is { ShipmentNumber: "SH-0000001", Kind: StopKindDto.Delivery }).CanComplete.Should().BeTrue();
        stops.Single(stop => stop is { ShipmentNumber: "SH-0000002", Kind: StopKindDto.Delivery }).CanComplete.Should().BeFalse();
    }

    [Fact]
    public async Task AFailedCollection_ClosesItsDelivery_ForGood()
    {
        _world.StartTodaysTrip();
        Report(ReportKind.CollectionFailed, StopKind.Collection, "SH-0000001");

        var delivery = (await Trip()).Stops.Single(stop => stop is { ShipmentNumber: "SH-0000001", Kind: StopKindDto.Delivery });

        delivery.CanComplete.Should().BeFalse();
        delivery.Blocked.Should().Be("The collection failed, so there is nothing to deliver.");
    }

    [Fact]
    public async Task ARefusedCollection_ReopensTheStop_AndKeepsTheRefusalToShow()
    {
        _world.StartTodaysTrip();
        Report(ReportKind.Collected, StopKind.Collection, "SH-0000001", ReportState.Refused);

        var stops = (await Trip()).Stops;

        var collection = stops.Single(stop => stop is { ShipmentNumber: "SH-0000001", Kind: StopKindDto.Collection });
        collection.Completed.Should().BeFalse();
        collection.CanComplete.Should().BeTrue("a refused report can be sent again");
        collection.Outcome!.State.Should().Be(ReportStateDto.Refused);
        collection.Outcome.FailureCode.Should().Be("ShipmentNotOnTrip");
        stops.Single(stop => stop is { ShipmentNumber: "SH-0000001", Kind: StopKindDto.Delivery }).CanComplete.Should().BeFalse("the collection is not done");
    }

    [Fact]
    public async Task Damage_IsListedOnItsStop_WithoutGivingItAnOutcome()
    {
        _world.StartTodaysTrip();
        var damage = Report(ReportKind.Damaged, StopKind.Delivery, "SH-0000002", ReportState.Sending);

        var stop = (await Trip()).Stops.Single(stop => stop is { ShipmentNumber: "SH-0000002", Kind: StopKindDto.Delivery });

        stop.Outcome.Should().BeNull();
        stop.Reports.Should().ContainSingle().Which.Should().Match<TripReportView>(report => report.Id == damage.Id.Value && report.Remark == "A remark");
    }

    [Fact]
    public async Task ANewVersion_KeepsTheOutcomesAndReportStatesOfCompletedStops()
    {
        _world.StartTodaysTrip();
        var collected = Report(ReportKind.Collected, StopKind.Collection, "SH-0000001");
        var failed = Report(ReportKind.CollectionFailed, StopKind.Collection, "SH-0000002", ReportState.Sending);
        _world.GiveTodaysTripStops("SH-0000001", "SH-0000002", "SH-0000003");

        var trip = await Trip();

        trip.Version.Should().Be(4);
        trip.Stops.Single(stop => stop is { ShipmentNumber: "SH-0000001", Kind: StopKindDto.Collection }).Outcome!.Id.Should().Be(collected.Id.Value);
        trip.Stops.Single(stop => stop is { ShipmentNumber: "SH-0000002", Kind: StopKindDto.Collection }).Outcome!.State.Should().Be(ReportStateDto.Sending);
        failed.Id.Value.Should().NotBeEmpty();
        trip.Stops.Where(stop => stop.TakenOff).Should().BeEmpty();
        trip.Stops.Should().HaveCount(6);
    }

    [Fact]
    public async Task ACompletedStopWhoseShipmentWasTakenOff_IsKeptAndMarked_NotSilentlyRemoved()
    {
        _world.StartTodaysTrip();
        var collected = Report(ReportKind.Collected, StopKind.Collection, "SH-0000002");
        _world.GiveTodaysTripStops("SH-0000001");

        var stops = (await Trip()).Stops;

        stops.Select(stop => (stop.ShipmentNumber, stop.Kind)).Should().Equal(
            ("SH-0000001", StopKindDto.Collection), ("SH-0000001", StopKindDto.Delivery), ("SH-0000002", StopKindDto.Collection));
        var takenOff = stops[2];
        takenOff.TakenOff.Should().BeTrue();
        takenOff.Details.Should().BeNull();
        takenOff.Outcome!.Id.Should().Be(collected.Id.Value);
        takenOff.CanComplete.Should().BeFalse();
        takenOff.CanReportDamage.Should().BeFalse();
    }

    [Fact]
    public async Task ARefusedShipmentNotOnTrip_ShowsAsRefused_OnTheStopTakenOff()
    {
        _world.StartTodaysTrip();
        Report(ReportKind.Collected, StopKind.Collection, "SH-0000002", ReportState.Refused);
        _world.GiveTodaysTripStops("SH-0000001");

        var takenOff = (await Trip()).Stops.Single(stop => stop.TakenOff);

        takenOff.Outcome!.State.Should().Be(ReportStateDto.Refused);
        takenOff.Outcome.FailureCode.Should().Be("ShipmentNotOnTrip");
    }

    [Fact]
    public async Task AShipmentTakenOffWithNothingCompleted_LeavesNoTraceButDamage_WhichAnyStopMayHave()
    {
        _world.StartTodaysTrip();
        Report(ReportKind.Damaged, StopKind.Collection, "SH-0000002");
        _world.GiveTodaysTripStops("SH-0000001");

        var trip = await Trip();

        trip.Stops.Should().NotContain(stop => stop.TakenOff);
        trip.Reports.Should().Contain(report => report.Kind == ReportKindDto.Damaged, "the Trip still lists every report");
    }

    [Fact]
    public async Task TheStopScreen_ShowsOneStopOfTheDriversOwnTrip_AndNothingOfAnotherDriversOrAnUnknownOne()
    {
        _world.StartTodaysTrip();
        Report(ReportKind.Collected, StopKind.Collection, "SH-0000001");
        var handler = new GetDriverStopHandler(_world.TripRepository, _world.ReadReportRepository);

        var mine = await handler.Handle(new GetDriverStopQuery("DRV-0002", "TR-0000012", "SH-0000001", StopKindDto.Collection), CancellationToken.None);
        var delivery = await handler.Handle(new GetDriverStopQuery("DRV-0002", "TR-0000012", "SH-0000001", StopKindDto.Delivery), CancellationToken.None);
        var anothers = await handler.Handle(new GetDriverStopQuery("DRV-0001", "TR-0000012", "SH-0000001", StopKindDto.Collection), CancellationToken.None);
        var unknown = await handler.Handle(new GetDriverStopQuery("DRV-0002", "TR-0000012", "SH-0000099", StopKindDto.Collection), CancellationToken.None);

        mine.Should().NotBeNull();
        mine!.Status.Should().Be("In progress");
        mine.Version.Should().Be(3);
        mine.Stop.Completed.Should().BeTrue();
        delivery!.Stop.CanComplete.Should().BeTrue();
        delivery.Stop.Details!.Consignee!.Name.Should().Be("Bakker BV");
        anothers.Should().BeNull();
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task TheStopScreenOfATripNotYetStarted_ShowsDispatched_AndNoTripAtAllIsNotFound()
    {
        var handler = new GetDriverStopHandler(_world.TripRepository, _world.ReadReportRepository);

        var open = await handler.Handle(new GetDriverStopQuery("DRV-0002", "TR-0000012", "SH-0000001", StopKindDto.Collection), CancellationToken.None);
        var noTrip = await handler.Handle(new GetDriverStopQuery("DRV-0002", "TR-0000099", "SH-0000001", StopKindDto.Collection), CancellationToken.None);

        open!.Status.Should().Be("Dispatched");
        open.Stop.Blocked.Should().Be("Start the Trip first.");
        noTrip.Should().BeNull();
    }
}
