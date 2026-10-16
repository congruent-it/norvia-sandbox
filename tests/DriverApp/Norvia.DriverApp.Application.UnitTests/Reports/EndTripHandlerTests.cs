using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Reports.EndTrip;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>The Driver presses End Trip (P12): the Driver app records the report and publishes exactly one <c>TripEndReported</c> through its outbox.</summary>
public sealed class EndTripHandlerTests
{
    private readonly ReportsWorld _world = new();

    private Task<Ardalis.Result.Result<EndTripResult>> End(string trip, string driver) =>
        new EndTripHandler(_world.TripRepository, _world.ReportRepository, _world.Outbox, _world.Time)
            .Handle(new EndTripCommand(trip, driver), CancellationToken.None).AsTask();

    private void EveryStopHasAnOutcome()
    {
        _world.GiveTodaysTripStops("SH-0000001");
        _world.StartTodaysTrip();
        foreach (var (kind, stop) in new[] { (ReportKind.Collected, StopKind.Collection), (ReportKind.Delivered, StopKind.Delivery) })
        {
            var report = DriverReport.ReportStop(
                new ReportId(Guid.NewGuid()), kind == ReportKind.Delivered ? ReportKind.DeliveryFailed : kind, new StopRef("SH-0000001", stop),
                new ReportOrigin(ReportsWorld.TripToday, ReportsWorld.Joost, 3, _world.Time.GetUtcNow(), "corr"), "Closed");
            report.Record(_world.Time.GetUtcNow());
            _world.Reports.Add(report);
        }
    }

    [Fact]
    public async Task EndingATripWhoseStopsAllHaveAnOutcome_RecordsASendingReport_AndQueuesOneEventWithTheDriverIdFromTheToken()
    {
        EveryStopHasAnOutcome();

        var result = await End("TR-0000012", "DRV-0002");

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyReported.Should().BeFalse();
        var report = _world.Reports.Single(report => report.Kind == ReportKind.EndTrip);
        report.Id.Value.Should().Be(result.Value.ReportId);
        report.State.Should().Be(ReportState.Sending);
        report.DriverId.Should().Be(ReportsWorld.Joost);
        report.VersionHeld.Should().Be(3);
        report.EventTime.Should().Be(_world.Time.GetUtcNow());

        var published = _world.Saved.Should().ContainSingle().Which.Should().BeOfType<TripEndReportedV1>().Subject;
        published.EventId.Should().Be(report.Id.Value);
        published.TripNumber.Should().Be("TR-0000012");
        published.Version.Should().Be(3);
        published.DriverId.Should().Be("DRV-0002");
        published.OccurredAt.Should().Be(report.EventTime);
        published.CorrelationId.Should().Be(report.CorrelationId).And.NotBeNullOrWhiteSpace();
        result.Value.CorrelationId.Should().Be(report.CorrelationId, "the answer names the trail the report travels on");
    }

    [Fact]
    public async Task EndingWhileAStopIsOpen_IsAConflict_AndPublishesNothing()
    {
        _world.GiveTodaysTripStops("SH-0000001");
        _world.StartTodaysTrip();

        var result = await End("TR-0000012", "DRV-0002");

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Should().Be("Give every Stop an outcome first.");
        _world.Queued.Should().BeEmpty();
        await _world.ReportRepository.DidNotReceive().AddAsync(Arg.Any<DriverReport>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EndingATripThatHasNotStarted_IsAConflict_AndPublishesNothing()
    {
        var result = await End("TR-0000012", "DRV-0002");

        result.Status.Should().Be(Ardalis.Result.ResultStatus.Conflict);
        result.Errors.Should().ContainSingle().Which.Should().Be("Start the Trip first.");
        _world.Queued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndingAgainWhileTheFirstIsSendingOrRecorded_ReturnsTheFirst_AndPublishesNothingMore(bool recorded)
    {
        EveryStopHasAnOutcome();
        var first = await End("TR-0000012", "DRV-0002");
        if (recorded)
        {
            _world.Reports.Single(report => report.Kind == ReportKind.EndTrip).Record(_world.Time.GetUtcNow());
        }

        var again = await End("TR-0000012", "DRV-0002");

        again.Value.Should().Be(new EndTripResult(first.Value.ReportId, AlreadyReported: true));
        _world.Queued.Should().ContainSingle();
    }

    [Fact]
    public async Task EndingAgainAfterARefusal_ReportsAgain()
    {
        EveryStopHasAnOutcome();
        await End("TR-0000012", "DRV-0002");
        _world.Reports.Single(report => report.Kind == ReportKind.EndTrip).Refuse("StopsOpen", "A Stop is open.", _world.Time.GetUtcNow());

        var again = await End("TR-0000012", "DRV-0002");

        again.Value.AlreadyReported.Should().BeFalse();
        _world.Queued.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnotherDriversTrip_AndAnUnknownTrip_AreNotFound_AndPublishNothing()
    {
        EveryStopHasAnOutcome();

        (await End("TR-0000012", "DRV-0001")).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
        (await End("TR-9999999", "DRV-0002")).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);

        _world.Queued.Should().BeEmpty();
    }
}
