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
