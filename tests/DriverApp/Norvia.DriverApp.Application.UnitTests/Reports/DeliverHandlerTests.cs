using Ardalis.Result;
using Norvia.DriverApp.Application.Events;
using Norvia.DriverApp.Application.Reports.Deliver;
using Norvia.DriverApp.Domain.Reports;
using NSubstitute;

namespace Norvia.DriverApp.Application.UnitTests.Reports;

/// <summary>
/// The Driver delivers a Stop with a Proof of delivery (P12): the Driver app stores the files in the transit container, records two reports and publishes
/// exactly two events through its outbox, <c>StopCompletedReported</c> (Delivered) and <c>ProofOfDeliveryCaptured</c>, with one correlation ID. The TMS still decides.
/// </summary>
public sealed class DeliverHandlerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 1, 2, 3];

    private readonly ReportsWorld _world = new();
    private readonly IProofOfDeliveryTransit _transit = Substitute.For<IProofOfDeliveryTransit>();
    private readonly List<(string Name, string ContentType, byte[] Content)> _stored = [];

    public DeliverHandlerTests()
    {
        _world.GiveTodaysTripStops("SH-0000001", "SH-0000002");
        _world.StartTodaysTrip();
        CollectBoth();
        _transit.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _stored.Add((call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<ReadOnlyMemory<byte>>(2).ToArray()));
                return Task.FromResult(call.ArgAt<string>(0));
            });
    }

    private void CollectBoth()
    {
        foreach (var shipment in new[] { "SH-0000001", "SH-0000002" })
        {
            var collected = DriverReport.ReportStop(
                new ReportId(Guid.NewGuid()), ReportKind.Collected, new(shipment, Domain.Trips.StopKind.Collection), new(ReportsWorld.TripToday, ReportsWorld.Joost, 3, _world.Time.GetUtcNow(), "c"), null);
            collected.Record(_world.Time.GetUtcNow());
            _world.Reports.Add(collected);
        }
    }

    private static DeliverCommand Command(
        string shipment = "SH-0000001", string? receivedBy = "J. Bakker", DeliveryConditionDto? condition = DeliveryConditionDto.Clean, string? remark = null,
        bool signature = true, int photos = 0, string driver = "DRV-0002", string trip = "TR-0000012") =>
        new(trip, driver, shipment, receivedBy, condition, remark, signature ? new ProofFile("image/png", Png) : null, [.. Enumerable.Repeat(new ProofFile("image/jpeg", Jpeg), photos)]);

    private Task<Result<DeliverResult>> Deliver(DeliverCommand command) =>
        new DeliverHandler(_world.TripRepository, _world.ReportRepository, _transit, _world.Outbox, _world.Time).Handle(command, CancellationToken.None).AsTask();

    [Fact]
    public async Task Delivering_RecordsADeliveredAndAProofOfDelivery_BothSending_AndPublishesTwoEventsWithOneCorrelationId()
    {
        var result = await Deliver(Command(photos: 1));

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyReported.Should().BeFalse();
        var delivered = _world.Reports.Single(report => report.Kind == ReportKind.Delivered);
        var proof = _world.Reports.Single(report => report.Kind == ReportKind.ProofOfDelivery);
        delivered.Id.Value.Should().Be(result.Value.ReportId);
        new[] { delivered, proof }.Should().OnlyContain(report => report.State == ReportState.Sending && report.CorrelationId == delivered.CorrelationId);

        _world.Saved.Should().HaveCount(2);
        var stop = _world.Saved[0].Should().BeOfType<StopCompletedReportedV1>().Subject;
        var captured = _world.Saved[1].Should().BeOfType<ProofOfDeliveryCapturedV1>().Subject;
        stop.EventId.Should().Be(delivered.Id.Value);
        stop.Stop.Should().Be("Delivery");
        stop.TrackingEventType.Should().Be("Delivered");
        stop.Location.Should().Be(new StopLocationV1("Rotterdam", "3087 BM", "NL"));
        captured.EventId.Should().Be(proof.Id.Value);
        captured.CorrelationId.Should().Be(stop.CorrelationId).And.Be(delivered.CorrelationId);
        captured.DriverId.Should().Be("DRV-0002");
        captured.TripNumber.Should().Be("TR-0000012");
        captured.Version.Should().Be(3);
        captured.ShipmentNumber.Should().Be("SH-0000001");
        captured.ReceivedBy.Should().Be("J. Bakker");
        captured.Condition.Should().Be("Clean");
        captured.Remark.Should().BeNull();
        captured.DeliveryTime.Should().Be(_world.Time.GetUtcNow());
        captured.Location.Should().Be(stop.Location);
        captured.DocumentId.Should().Be(proof.Id.Value.ToString());
        result.Value.CorrelationId.Should().Be(delivered.CorrelationId, "the answer names the trail the report travels on");
    }

    [Fact]
    public async Task TheFiles_GoToTheTransitContainer_AndTheEventCarriesOnlyTheirBlobReferences_SignatureFirst()
    {
        await Deliver(Command(photos: 2));

        var captured = _world.Saved.OfType<ProofOfDeliveryCapturedV1>().Single();
        _stored.Select(file => file.Name).Should().Equal(
            $"{captured.DocumentId}/signature.png", $"{captured.DocumentId}/photo-1.jpg", $"{captured.DocumentId}/photo-2.jpg");
        _stored[0].Content.Should().Equal(Png);
        _stored[1].ContentType.Should().Be("image/jpeg");
        captured.Files.Should().Equal(
            new ProofOfDeliveryFileV1("signature.png", "image/png", $"{captured.DocumentId}/signature.png"),
            new ProofOfDeliveryFileV1("photo-1.jpg", "image/jpeg", $"{captured.DocumentId}/photo-1.jpg"),
            new ProofOfDeliveryFileV1("photo-2.jpg", "image/jpeg", $"{captured.DocumentId}/photo-2.jpg"));
    }

    [Fact]
    public async Task WithReservations_TheRemarkGoesOnTheProofOfDelivery_AndTheDeliveredReport()
    {
        await Deliver(Command(condition: DeliveryConditionDto.Reservations, remark: "  Two cartons crushed "));

        var captured = _world.Saved.OfType<ProofOfDeliveryCapturedV1>().Single();
        captured.Condition.Should().Be("Reservations");
        captured.Remark.Should().Be("Two cartons crushed");
        _world.Reports.Single(report => report.Kind == ReportKind.Delivered).Remark.Should().Be("Two cartons crushed");
    }

    [Fact]
    public async Task ARemarkOnACleanDelivery_IsNotKept()
    {
        await Deliver(Command(remark: "ignored"));

        _world.Saved.OfType<ProofOfDeliveryCapturedV1>().Single().Remark.Should().BeNull();
    }

    [Fact]
    public async Task ADeliveryWithoutAProof_IsInvalid_StoresNothingAndPublishesNothing()
    {
        var result = await Deliver(Command(receivedBy: null, signature: false));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Select(error => error.Identifier).Should().Equal("receivedBy", "signature");
        _stored.Should().BeEmpty();
        _world.Saved.Should().BeEmpty();
        _world.Reports.Should().NotContain(report => report.Kind == ReportKind.Delivered);
    }

    [Fact]
    public async Task ReservationsWithoutARemark_AreInvalid()
    {
        var result = await Deliver(Command(condition: DeliveryConditionDto.Reservations));

        result.ValidationErrors.Select(error => error.Identifier).Should().Equal("remark");
    }

    [Fact]
    public async Task ADeliveryBeforeItsCollection_IsRefused_BeforeAnyFileIsStored()
    {
        _world.Reports.RemoveAll(report => report.Kind == ReportKind.Collected);

        var result = await Deliver(Command());

        result.Status.Should().Be(ResultStatus.Conflict);
        _stored.Should().BeEmpty();
        _world.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task DeliveringAgainWhileTheFirstIsOnItsWay_PublishesAndStoresNothing()
    {
        var first = await Deliver(Command());
        var second = await Deliver(Command());

        second.IsSuccess.Should().BeTrue();
        second.Value.AlreadyReported.Should().BeTrue();
        second.Value.ReportId.Should().Be(first.Value.ReportId);
        _world.Saved.Should().HaveCount(2);
        _stored.Should().ContainSingle();
    }

    [Fact]
    public async Task AfterTheTmsRefusedTheDelivered_TheDriverCanDeliverAgain_WithANewDocument()
    {
        var first = await Deliver(Command());
        _world.Reports.Single(report => report.Id.Value == first.Value.ReportId).Refuse("ShipmentNotOnTrip", "Taken off.", _world.Time.GetUtcNow());

        var second = await Deliver(Command());

        second.IsSuccess.Should().BeTrue();
        second.Value.AlreadyReported.Should().BeFalse();
        _world.Saved.OfType<ProofOfDeliveryCapturedV1>().Select(captured => captured.DocumentId).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task AStopThatIsNotOnTheDriversTrips_IsNotFound()
    {
        (await Deliver(Command(driver: "DRV-0001"))).Status.Should().Be(ResultStatus.NotFound);
        (await Deliver(Command(shipment: "SH-0000099"))).Status.Should().Be(ResultStatus.NotFound);
        (await Deliver(Command(trip: "TR-0000099"))).Status.Should().Be(ResultStatus.NotFound);
        _world.Saved.Should().BeEmpty();
    }
}
