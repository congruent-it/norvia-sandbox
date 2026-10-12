using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Reports.StartTrip;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>The Driver presses Start Trip (P12): the Driver app records the report and publishes exactly one <c>TripStartReported</c> through its outbox.</summary>
public sealed class StartTripHandlerTests
{
    private readonly ReportsWorld _world = new();

    private Task<Ardalis.Result.Result<StartTripResult>> Start(string trip, string driver) =>
        new StartTripHandler(_world.TripRepository, _world.ReportRepository, _world.Outbox, _world.Time)
            .Handle(new StartTripCommand(trip, driver), CancellationToken.None).AsTask();

    [Fact]
    public async Task StartingADispatchedTrip_RecordsASendingReport_AndQueuesOneEventWithTheDriverIdFromTheToken()
    {
        var result = await Start("TR-0000012", "DRV-0002");

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyReported.Should().BeFalse();
        var report = _world.Reports.Should().ContainSingle().Subject;
        report.Id.Value.Should().Be(result.Value.ReportId);
        report.State.Should().Be(ReportState.Sending);
        report.Kind.Should().Be(ReportKind.StartTrip);
        report.TripNumber.Should().Be(ReportsWorld.TripToday);
        report.DriverId.Should().Be(ReportsWorld.Joost);
        report.VersionHeld.Should().Be(2);
        report.EventTime.Should().Be(_world.Time.GetUtcNow());

        var published = _world.Saved.Should().ContainSingle().Which.Should().BeOfType<TripStartReportedV1>().Subject;
        published.EventId.Should().Be(report.Id.Value);
        published.TripNumber.Should().Be("TR-0000012");
        published.Version.Should().Be(2);
        published.DriverId.Should().Be("DRV-0002");
        published.OccurredAt.Should().Be(report.EventTime);
        published.CorrelationId.Should().Be(report.CorrelationId).And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheEvent_IsQueuedBeforeTheReportIsSaved_SoBothAreSavedInOneTransaction()
    {
        await Start("TR-0000012", "DRV-0002");

        _world.Saved.Should().HaveCount(1, "the outbox message was queued when the save happened");
    }

    [Fact]
    public async Task EachActionGetsANewCorrelationId()
    {
        var first = await Start("TR-0000012", "DRV-0002");
        _world.Reports.Single().Refuse("NotTripDay", "no", _world.Time.GetUtcNow());
        var second = await Start("TR-0000012", "DRV-0002");

        first.Value.ReportId.Should().NotBe(second.Value.ReportId);
        _world.Reports.Select(report => report.CorrelationId).Distinct().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartingAgainWhileTheFirstIsSendingOrRecorded_ReturnsTheFirst_AndPublishesNothingMore(bool recorded)
    {
        var first = await Start("TR-0000012", "DRV-0002");
        if (recorded)
        {
            _world.Reports.Single().Record(_world.Time.GetUtcNow());
        }

        var again = await Start("TR-0000012", "DRV-0002");

        again.Value.Should().Be(new StartTripResult(first.Value.ReportId, AlreadyReported: true));
        _world.Reports.Should().ContainSingle();
        _world.Queued.Should().ContainSingle();
    }

    [Theory]
    [InlineData(ReportState.Refused)]
    [InlineData(ReportState.NotRecorded)]
    public async Task StartingAgainAfterARefusalOrAFailure_ReportsAgain(ReportState outcome)
    {
        await Start("TR-0000012", "DRV-0002");
        var failed = _world.Reports.Single();
        _ = outcome == ReportState.Refused
            ? failed.Refuse("NotTripDay", "Wrong day.", _world.Time.GetUtcNow())
            : failed.GiveUp("TmsUnavailable", "The TMS was down.", _world.Time.GetUtcNow());

        var again = await Start("TR-0000012", "DRV-0002");

        again.Value.AlreadyReported.Should().BeFalse();
        _world.Reports.Should().HaveCount(2);
        _world.Queued.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnotherDriversTrip_AndAnUnknownTrip_AreNotFound_AndPublishNothing()
    {
        (await Start("TR-0000012", "DRV-0001")).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
        (await Start("TR-9999999", "DRV-0002")).Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);

        _world.Reports.Should().BeEmpty();
        _world.Queued.Should().BeEmpty();
        await _world.ReportRepository.DidNotReceive().AddAsync(Arg.Any<DriverReport>(), Arg.Any<CancellationToken>());
    }
}
