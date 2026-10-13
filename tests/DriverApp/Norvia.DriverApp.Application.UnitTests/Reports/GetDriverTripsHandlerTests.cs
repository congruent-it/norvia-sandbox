using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Application.Trips.GetForDriver;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>What My Trips shows a Driver: their Trips with the state of each report, and whether they can start one (P12).</summary>
public sealed class GetDriverTripsHandlerTests
{
    private readonly ReportsWorld _world = new();

    private async Task<IReadOnlyList<DriverTripView>> TripsOf(string driver) =>
        await new GetDriverTripsHandler(_world.TripRepository, _world.ReadReportRepository, _world.Time)
            .Handle(new GetDriverTripsQuery(driver), CancellationToken.None);

    private DriverReport Report(string trip = "TR-0000012", int minutes = 0)
    {
        var report = DriverReport.StartTrip(new ReportId(Guid.NewGuid()), new TripNumber(trip), ReportsWorld.Joost, 2, _world.Time.GetUtcNow().AddMinutes(minutes), "corr");
        _world.Reports.Add(report);
        return report;
    }

    [Fact]
    public async Task ADispatchedTripOnItsDay_CanBeStarted()
    {
        var trips = await TripsOf("DRV-0002");

        var today = trips.Single(trip => trip.TripNumber == "TR-0000012");
        today.Status.Should().Be("Dispatched");
        today.CanStart.Should().BeTrue();
        today.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task ADispatchedTripOnAnotherDay_CannotBeStarted_ByTheDriverApp()
    {
        var trips = await TripsOf("DRV-0002");

        trips.Single(trip => trip.TripNumber == "TR-0000013").CanStart.Should().BeFalse();
    }

    [Fact]
    public async Task TheDayIsCountedInNorviasTime_NotUtc()
    {
        // 22:30 UTC on 13 October is 00:30 on 14 October in Amsterdam.
        _world.Time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 22, 30, 0, TimeSpan.Zero));

        var trips = await TripsOf("DRV-0002");

        trips.Single(trip => trip.TripNumber == "TR-0000012").CanStart.Should().BeFalse();
        trips.Single(trip => trip.TripNumber == "TR-0000013").CanStart.Should().BeTrue();
    }

    [Fact]
    public async Task ASendingStart_ShowsAsSending_AndCannotBeStartedAgain()
    {
        var report = Report();

        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.Status.Should().Be("Dispatched");
        trip.CanStart.Should().BeFalse();
        trip.Reports.Should().ContainSingle().Which.Should().Be(new TripReportView(report.Id.Value, ReportKindDto.StartTrip, ReportStateDto.Sending, null, null, report.EventTime));
    }

    [Fact]
    public async Task ARecordedStart_MakesTheTripInProgress()
    {
        Report().Record(_world.Time.GetUtcNow());

        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.Status.Should().Be("In progress");
        trip.CanStart.Should().BeFalse();
        trip.Reports.Single().State.Should().Be(ReportStateDto.Recorded);
    }

    [Fact]
    public async Task WhileAStopIsOpen_EndTripIsUnavailable_AndSaysWhy()
    {
        _world.GiveTodaysTripStops("SH-0000001");
        _world.StartTodaysTrip();

        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.CanEnd.Should().BeFalse();
        trip.EndUnavailable.Should().Be("Give every Stop an outcome first.");
    }

    [Fact]
    public async Task BeforeTheStart_EndTripIsNeitherOfferedNorExplained()
    {
        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.CanEnd.Should().BeFalse();
        trip.EndUnavailable.Should().BeNull();
    }

    [Fact]
    public async Task OnceEveryStopHasAnOutcome_EndTripIsOffered_UntilTheEndIsSendingOrRecorded_AndTheRecordedEndMakesTheTripCompleted()
    {
        _world.GiveTodaysTripStops("SH-0000001");
        _world.StartTodaysTrip();
        foreach (var (kind, stop) in new[] { (ReportKind.Collected, StopKind.Collection), (ReportKind.DeliveryFailed, StopKind.Delivery) })
        {
            var report = DriverReport.ReportStop(
                new ReportId(Guid.NewGuid()), kind, new StopRef("SH-0000001", stop), new ReportOrigin(ReportsWorld.TripToday, ReportsWorld.Joost, 3, _world.Time.GetUtcNow(), "corr"), "Closed");
            report.Record(_world.Time.GetUtcNow());
            _world.Reports.Add(report);
        }

        var before = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");
        var end = DriverReport.EndTrip(new ReportId(Guid.NewGuid()), ReportsWorld.TripToday, ReportsWorld.Joost, 3, _world.Time.GetUtcNow(), "end-corr");
        _world.Reports.Add(end);
        var sending = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");
        end.Record(_world.Time.GetUtcNow());
        var recorded = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        before.CanEnd.Should().BeTrue();
        before.EndUnavailable.Should().BeNull();
        sending.CanEnd.Should().BeFalse();
        sending.Status.Should().Be("In progress");
        sending.Reports[^1].Kind.Should().Be(ReportKindDto.EndTrip);
        recorded.Status.Should().Be("Completed");
        recorded.CanEnd.Should().BeFalse();
    }

    [Fact]
    public async Task ARefusedStart_ShowsItsCodeAndMessage_AndCanBeSentAgain()
    {
        Report().Refuse("NotTripDay", "Trip TR-0000012 is for another day.", _world.Time.GetUtcNow());

        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.Status.Should().Be("Dispatched");
        trip.CanStart.Should().BeTrue();
        var report = trip.Reports.Single();
        report.State.Should().Be(ReportStateDto.Refused);
        report.FailureCode.Should().Be("NotTripDay");
        report.Message.Should().Be("Trip TR-0000012 is for another day.");
    }

    [Fact]
    public async Task ANotRecordedStart_ShowsAsNotRecorded_AndCanBeSentAgain()
    {
        Report().GiveUp("TmsUnavailable", "The TMS answered 503 (4 attempts).", _world.Time.GetUtcNow());

        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.CanStart.Should().BeTrue();
        trip.Reports.Single().State.Should().Be(ReportStateDto.NotRecorded);
    }

    [Fact]
    public async Task EachTripShowsItsOwnReports_OldestFirst()
    {
        var late = Report(minutes: 5);
        var early = Report(minutes: 1);
        Report("TR-0000013", 2);

        var trip = (await TripsOf("DRV-0002")).Single(trip => trip.TripNumber == "TR-0000012");

        trip.Reports.Select(report => report.Id).Should().Equal(early.Id.Value, late.Id.Value);
    }

    [Fact]
    public async Task ADriverSeesOnlyTheirOwnTrips_AndNoneWithoutTrips()
    {
        (await TripsOf("DRV-0001")).Select(trip => trip.TripNumber).Should().Equal("TR-0000014");
        (await TripsOf("DRV-0009")).Should().BeEmpty();
    }
}
