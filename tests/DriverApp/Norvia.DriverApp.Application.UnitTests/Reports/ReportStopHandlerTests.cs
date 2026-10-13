using Ardalis.Result;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Reports;
using Norvia.DriverApp.Application.Reports.ReportStop;
using Norvia.DriverApp.Application.Trips;
using Norvia.DriverApp.Domain.Reports;
using Norvia.DriverApp.Domain.Trips;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>
/// The Driver reports on a Stop (P12): the Driver app records the report and publishes exactly one <c>StopCompletedReported</c> through its outbox. It offers
/// only what the Driver can do now: a Stop is completed once, in any order, but a delivery waits for its collection. The TMS still decides.
/// </summary>
public sealed class ReportStopHandlerTests
{
    private readonly ReportsWorld _world = new();

    public ReportStopHandlerTests()
    {
        _world.GiveTodaysTripStops("SH-0000001", "SH-0000002");
        _world.StartTodaysTrip();
    }

    private Task<Result<ReportStopResult>> Report(StopKindDto stop, StopActionDto action, string shipment = "SH-0000001", string? remark = null, string driver = "DRV-0002", string trip = "TR-0000012") =>
        new ReportStopHandler(_world.TripRepository, _world.ReportRepository, _world.Outbox, _world.Time)
            .Handle(new ReportStopCommand(trip, driver, shipment, stop, action, remark), CancellationToken.None).AsTask();

    private DriverReport Seed(ReportKind kind, StopKind stop, string shipment = "SH-0000001", ReportState state = ReportState.Recorded)
    {
        var report = DriverReport.ReportStop(
            new ReportId(Guid.NewGuid()), kind, ReportsWorld.TripToday, shipment, stop, ReportsWorld.Joost, 3, _world.Time.GetUtcNow(), "corr", kind == ReportKind.Collected ? null : "remark");
        _ = state switch
        {
            ReportState.Recorded => report.Record(_world.Time.GetUtcNow()),
            ReportState.Refused => report.Refuse("ShipmentNotOnTrip", "No longer on the Trip.", _world.Time.GetUtcNow()),
            ReportState.NotRecorded => report.GiveUp(null, "gave up", _world.Time.GetUtcNow()),
            _ => false,
        };
        _world.Reports.Add(report);
        return report;
    }

    [Fact]
    public async Task CollectingAShipment_RecordsASendingReport_AndQueuesOneEventWithTheDriverIdFromTheToken_AndTheStopsAddress()
    {
        var result = await Report(StopKindDto.Collection, StopActionDto.Collected);

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyReported.Should().BeFalse();
        var report = _world.Reports.Single(report => report.Kind == ReportKind.Collected);
        report.Id.Value.Should().Be(result.Value.ReportId);
        report.State.Should().Be(ReportState.Sending);
        report.ShipmentNumber.Should().Be("SH-0000001");
        report.Stop.Should().Be(StopKind.Collection);
        report.VersionHeld.Should().Be(3);
        report.DriverId.Should().Be(ReportsWorld.Joost);

        var published = _world.Saved.Should().ContainSingle().Which.Should().BeOfType<StopCompletedReportedV1>().Subject;
        published.EventId.Should().Be(report.Id.Value);
        published.TripNumber.Should().Be("TR-0000012");
        published.Version.Should().Be(3);
        published.DriverId.Should().Be("DRV-0002");
        published.ShipmentNumber.Should().Be("SH-0000001");
        published.Stop.Should().Be("Collection");
        published.TrackingEventType.Should().Be("Collected");
        published.Location.Should().Be(new StopLocationV1("Rotterdam", "3087 BM", "NL"));
        published.Remark.Should().BeNull();
        published.CorrelationId.Should().Be(report.CorrelationId).And.NotBeNullOrWhiteSpace();
        published.OccurredAt.Should().Be(report.EventTime);
    }

    [Theory]
    [InlineData(StopKindDto.Collection, StopActionDto.CollectionFailed, "CollectionFailed")]
    [InlineData(StopKindDto.Collection, StopActionDto.Damaged, "Damaged")]
    public async Task AFailureOrDamageAtACollectionStop_CarriesTheRemark(StopKindDto stop, StopActionDto action, string trackingEventType)
    {
        var result = await Report(stop, action, remark: "  Nobody at the dock ");

        result.IsSuccess.Should().BeTrue();
        var published = _world.Saved.Should().ContainSingle().Which.Should().BeOfType<StopCompletedReportedV1>().Subject;
        published.TrackingEventType.Should().Be(trackingEventType);
        published.Remark.Should().Be("Nobody at the dock");
    }

    [Fact]
    public async Task ADeliveryFailedAfterTheCollection_IsReported_WhetherTheCollectionIsSendingOrRecorded()
    {
        var collected = Seed(ReportKind.Collected, StopKind.Collection, state: ReportState.Recorded);
        collected.Id.Value.Should().NotBeEmpty();
        Seed(ReportKind.Collected, StopKind.Collection, "SH-0000002", ReportState.Sending);

        var first = await Report(StopKindDto.Delivery, StopActionDto.DeliveryFailed, remark: "Closed");
        var second = await Report(StopKindDto.Delivery, StopActionDto.DeliveryFailed, "SH-0000002", "Closed");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        _world.Saved.Select(e => ((StopCompletedReportedV1)e).Stop).Should().Equal("Delivery", "Delivery");
    }

    [Fact]
    public async Task TheStopsCanBeCompletedInAnyOrder()
    {
        var second = await Report(StopKindDto.Collection, StopActionDto.Collected, "SH-0000002");
        var first = await Report(StopKindDto.Collection, StopActionDto.CollectionFailed, "SH-0000001", "Gone");

        second.IsSuccess.Should().BeTrue();
        first.IsSuccess.Should().BeTrue();
        _world.Saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task ADeliveryStop_IsUnavailableBeforeItsCollection_AndPublishesNothing()
    {
        var result = await Report(StopKindDto.Delivery, StopActionDto.DeliveryFailed, remark: "Closed");

        result.Status.Should().Be(ResultStatus.Conflict);
        _world.Saved.Should().BeEmpty();
        _world.Reports.Should().ContainSingle("only the Start Trip");
    }

    [Theory]
    [InlineData(ReportState.Refused)]
    [InlineData(ReportState.NotRecorded)]
    public async Task ADeliveryStop_StaysUnavailable_WhileTheCollectionWasRefusedOrLost(ReportState state)
    {
        Seed(ReportKind.Collected, StopKind.Collection, state: state);

        var result = await Report(StopKindDto.Delivery, StopActionDto.DeliveryFailed, remark: "Closed");

        result.Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task ADeliveryStop_IsUnavailable_WhenItsCollectionFailed()
    {
        Seed(ReportKind.CollectionFailed, StopKind.Collection);

        var result = await Report(StopKindDto.Delivery, StopActionDto.DeliveryFailed, remark: "Closed");

        result.Status.Should().Be(ResultStatus.Conflict);
        _world.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipmentWithOnlyItsDeliveryStop_ARedelivery_CanBeDeliveredWithoutACollection()
    {
        var trip = _world.Trips.Single(trip => trip.Id == ReportsWorld.TripToday);
        trip.ReplaceWith(trip.Version + 1, ReportsWorld.Joost, trip.Details with { Stops = [.. trip.Details.Stops.Where(stop => stop is { Kind: StopKind.Delivery, ShipmentNumber: "SH-0000001" })] });

        var result = await Report(StopKindDto.Delivery, StopActionDto.DeliveryFailed, remark: "Closed again");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AStopTheDriverCompletedAlready_CannotBeCompletedAgainDifferently_ButTheSameActionIsTheSameReport()
    {
        var first = await Report(StopKindDto.Collection, StopActionDto.Collected);
        var again = await Report(StopKindDto.Collection, StopActionDto.Collected);
        var other = await Report(StopKindDto.Collection, StopActionDto.CollectionFailed, remark: "Changed my mind");

        again.Value.AlreadyReported.Should().BeTrue();
        again.Value.ReportId.Should().Be(first.Value.ReportId);
        other.Status.Should().Be(ResultStatus.Conflict);
        _world.Saved.Should().ContainSingle();
    }

    [Theory]
    [InlineData(ReportState.Refused)]
    [InlineData(ReportState.NotRecorded)]
    public async Task AStopWhoseReportWasRefusedOrLost_CanBeReportedAgain(ReportState state)
    {
        Seed(ReportKind.Collected, StopKind.Collection, state: state);

        var result = await Report(StopKindDto.Collection, StopActionDto.Collected);

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyReported.Should().BeFalse();
        _world.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task ReportDamage_DoesNotCompleteTheStop_SoTheStopCanStillBeCompleted_OnAnyStop()
    {
        var damage = await Report(StopKindDto.Delivery, StopActionDto.Damaged, remark: "Crushed carton");
        var collected = await Report(StopKindDto.Collection, StopActionDto.Collected);

        damage.IsSuccess.Should().BeTrue("damage can be reported on a delivery Stop before its collection");
        collected.IsSuccess.Should().BeTrue();
        _world.Saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReportDamageTwiceWithTheSameRemarkWhileTheFirstIsSending_IsTheSameReport()
    {
        var first = await Report(StopKindDto.Collection, StopActionDto.Damaged, remark: "Crushed carton");
        var again = await Report(StopKindDto.Collection, StopActionDto.Damaged, remark: "Crushed carton");
        var another = await Report(StopKindDto.Collection, StopActionDto.Damaged, remark: "And a torn wrap");

        again.Value.ReportId.Should().Be(first.Value.ReportId);
        again.Value.AlreadyReported.Should().BeTrue();
        another.Value.AlreadyReported.Should().BeFalse();
        _world.Saved.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(StopKindDto.Collection, StopActionDto.CollectionFailed)]
    [InlineData(StopKindDto.Delivery, StopActionDto.DeliveryFailed)]
    [InlineData(StopKindDto.Collection, StopActionDto.Damaged)]
    public async Task AFailureOrDamageWithoutARemark_IsInvalid_AndPublishesNothing(StopKindDto stop, StopActionDto action)
    {
        Seed(ReportKind.Collected, StopKind.Collection);

        var blank = await Report(stop, action, remark: "  ");
        var missing = await Report(stop, action);

        blank.Status.Should().Be(ResultStatus.Invalid);
        blank.ValidationErrors.Should().ContainSingle().Which.Identifier.Should().Be("remark");
        missing.Status.Should().Be(ResultStatus.Invalid);
        _world.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData(StopKindDto.Delivery, StopActionDto.Collected)]
    [InlineData(StopKindDto.Delivery, StopActionDto.CollectionFailed)]
    [InlineData(StopKindDto.Collection, StopActionDto.DeliveryFailed)]
    public async Task AnActionThatDoesNotFitTheKindOfStop_IsInvalid(StopKindDto stop, StopActionDto action)
    {
        var result = await Report(stop, action, remark: "remark");

        result.Status.Should().Be(ResultStatus.Invalid);
        _world.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task ARemarkLongerThanTheTmsTakes_IsInvalid()
    {
        var result = await Report(StopKindDto.Collection, StopActionDto.CollectionFailed, remark: new string('x', 501));

        result.Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task ATripThatIsNotInProgress_OffersNoStopReports()
    {
        _world.Reports.Clear();

        var dispatched = await Report(StopKindDto.Collection, StopActionDto.Collected);
        _world.Reports.Add(DriverReport.StartTrip(new ReportId(Guid.NewGuid()), ReportsWorld.TripToday, ReportsWorld.Joost, 3, _world.Time.GetUtcNow(), "c"));
        var starting = await Report(StopKindDto.Collection, StopActionDto.Collected);

        dispatched.Status.Should().Be(ResultStatus.Conflict);
        starting.Status.Should().Be(ResultStatus.Conflict, "the TMS has not said the Trip started");
        _world.Saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("DRV-0001", "TR-0000012", "SH-0000001")]
    [InlineData("DRV-0002", "TR-0000099", "SH-0000001")]
    [InlineData("DRV-0002", "TR-0000012", "SH-0000099")]
    public async Task AnotherDriversTripAnUnknownTripOrAShipmentNotOnTheTrip_IsNotFound(string driver, string trip, string shipment)
    {
        var result = await Report(StopKindDto.Collection, StopActionDto.Collected, shipment, driver: driver, trip: trip);

        result.Status.Should().Be(ResultStatus.NotFound);
        _world.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AStopWithoutACompleteAddress_IsInvalid_BecauseTheTmsNeedsAPlace()
    {
        var trip = _world.Trips.Single(trip => trip.Id == ReportsWorld.TripToday);
        trip.ReplaceWith(trip.Version + 1, ReportsWorld.Joost, trip.Details with { Stops = [.. trip.Details.Stops.Select(stop => stop with { Address = new StopAddress("Waalhaven 1", null, "Rotterdam", "NL") })] });

        var result = await Report(StopKindDto.Collection, StopActionDto.Collected);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.Identifier.Should().Be("address");
        _world.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task EachActionGetsANewCorrelationId()
    {
        await Report(StopKindDto.Collection, StopActionDto.Collected);
        await Report(StopKindDto.Collection, StopActionDto.Collected, "SH-0000002");

        _world.Reports.Where(report => report.Kind == ReportKind.Collected).Select(report => report.CorrelationId).Distinct().Should().HaveCount(2);
    }
}
