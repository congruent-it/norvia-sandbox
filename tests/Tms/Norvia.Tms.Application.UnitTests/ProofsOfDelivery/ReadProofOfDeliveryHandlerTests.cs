using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

public sealed class ReadProofOfDeliveryHandlerTests
{
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<ProofOfDelivery> _proofsOfDelivery = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IProofOfDeliveryFiles _files = Substitute.For<IProofOfDeliveryFiles>();
    private readonly Shipment _shipment = ShipmentCarriedTo(ShipmentStatus.Delivered);

    public ReadProofOfDeliveryHandlerTests()
    {
        _shipments.FirstOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number == _shipment.Number), Arg.Any<CancellationToken>()).Returns(_shipment);
    }

    [Fact]
    public async Task Get_ShowsTheCurrentProofOfDelivery_WithItsFiles()
    {
        var proofOfDelivery = HasCurrent();

        var result = await new GetProofOfDeliveryHandler(_shipments, _proofsOfDelivery).Handle(new GetProofOfDeliveryQuery("SH-0000012"), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new ProofOfDeliveryView(
            proofOfDelivery.Id.Value, "SH-0000012", "CAR-0001", "doc-1", "J. Becker", DeliveryTime, "Düsseldorf", "40213", "DE",
            DeliveryCondition.Clean, "Signed at the dock", proofOfDelivery.ReceivedAt, "corr-pod", [new ProofOfDeliveryFileView(Cmr().Id, "cmr.pdf", "application/pdf", 4)], true));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("SH-0000099")]
    public async Task Get_ForAShipmentThatDoesNotExist_IsNotFound(string shipmentNumber)
    {
        var result = await new GetProofOfDeliveryHandler(_shipments, _proofsOfDelivery).Handle(new GetProofOfDeliveryQuery(shipmentNumber), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Get_ForAShipmentWithNoProofOfDelivery_IsNotFound()
    {
        var result = await new GetProofOfDeliveryHandler(_shipments, _proofsOfDelivery).Handle(new GetProofOfDeliveryQuery("SH-0000012"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetFile_StreamsTheFileTheProofOfDeliveryHolds()
    {
        HasCurrent();
        using var content = new MemoryStream([1, 2, 3, 4]);
        _files.OpenReadAsync(_shipment.Number, Cmr(), Arg.Any<CancellationToken>()).Returns(content);

        var result = await Handler().Handle(new GetProofOfDeliveryFileQuery("SH-0000012", Cmr().Id), CancellationToken.None);

        result.Value.Should().Be(new ProofOfDeliveryFileContent("cmr.pdf", "application/pdf", content));
    }

    [Fact]
    public async Task GetFile_ForAFileTheProofOfDeliveryDoesNotHold_IsNotFound()
    {
        HasCurrent();

        var result = await Handler().Handle(new GetProofOfDeliveryFileQuery("SH-0000012", Guid.NewGuid()), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetFile_ForAFileTheStorageHasLost_IsNotFound()
    {
        HasCurrent();
        _files.OpenReadAsync(_shipment.Number, Cmr(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await Handler().Handle(new GetProofOfDeliveryFileQuery("SH-0000012", Cmr().Id), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("SH-0000099")]
    public async Task GetFile_ForAShipmentThatDoesNotExist_IsNotFound(string shipmentNumber)
    {
        var result = await Handler().Handle(new GetProofOfDeliveryFileQuery(shipmentNumber, Cmr().Id), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetFile_StreamsAFileOfASupersededProofOfDeliveryToo()
    {
        var superseded = ProofOfDelivery.Record(_shipment, Report(files: [Cmr()]), new CorrelationId("corr-1"), new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));
        var current = ProofOfDelivery.Record(
            _shipment, Report(documentId: "doc-2", files: [Cmr() with { Id = Guid.NewGuid() }]), new CorrelationId("corr-2"), new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), superseded);
        _proofsOfDelivery.ListAsync(Arg.Is<ProofsOfDeliveryForShipmentSpec>(s => s.ShipmentId == _shipment.Id), Arg.Any<CancellationToken>()).Returns([superseded, current]);
        using var content = new MemoryStream([1, 2, 3, 4]);
        _files.OpenReadAsync(_shipment.Number, Cmr(), Arg.Any<CancellationToken>()).Returns(content);

        var result = await Handler().Handle(new GetProofOfDeliveryFileQuery("SH-0000012", Cmr().Id), CancellationToken.None);

        result.Value.Should().Be(new ProofOfDeliveryFileContent("cmr.pdf", "application/pdf", content));
    }

    [Fact]
    public async Task Timeline_ListsEveryProofOfDelivery_FlaggingTheCurrentOne()
    {
        var superseded = ProofOfDelivery.Record(_shipment, Report(), new CorrelationId("corr-1"), new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));
        var current = ProofOfDelivery.Record(_shipment, Report(documentId: "doc-2"), new CorrelationId("corr-2"), new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), superseded);
        _proofsOfDelivery.ListAsync(Arg.Any<ProofsOfDeliveryForShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([superseded, current]);

        var result = await new GetProofsOfDeliveryTimelineHandler(_shipments, _proofsOfDelivery)
            .Handle(new GetProofsOfDeliveryTimelineQuery("SH-0000012"), CancellationToken.None);

        result.Value.Select(view => (view.DocumentId, view.IsCurrent)).Should().Equal(("doc-1", false), ("doc-2", true));
        result.Value.Should().OnlyContain(view => view.Files.Count == 1 && view.ReceivedBy == "J. Becker");
    }

    [Fact]
    public async Task Timeline_ForAShipmentWithNoProofOfDelivery_IsEmpty()
    {
        _proofsOfDelivery.ListAsync(Arg.Any<ProofsOfDeliveryForShipmentSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await new GetProofsOfDeliveryTimelineHandler(_shipments, _proofsOfDelivery)
            .Handle(new GetProofsOfDeliveryTimelineQuery("SH-0000012"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("SH-0000099")]
    public async Task Timeline_ForAShipmentThatDoesNotExist_IsNotFound(string shipmentNumber)
    {
        var result = await new GetProofsOfDeliveryTimelineHandler(_shipments, _proofsOfDelivery)
            .Handle(new GetProofsOfDeliveryTimelineQuery(shipmentNumber), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    private GetProofOfDeliveryFileHandler Handler() => new(_shipments, _proofsOfDelivery, _files);

    private ProofOfDelivery HasCurrent()
    {
        var proofOfDelivery = ProofOfDelivery.Record(_shipment, Report(), new CorrelationId("corr-pod"), new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero));
        _proofsOfDelivery.SingleOrDefaultAsync(Arg.Is<CurrentProofOfDeliverySpec>(s => s.ShipmentId == _shipment.Id), Arg.Any<CancellationToken>()).Returns(proofOfDelivery);
        _proofsOfDelivery.ListAsync(Arg.Is<ProofsOfDeliveryForShipmentSpec>(s => s.ShipmentId == _shipment.Id), Arg.Any<CancellationToken>()).Returns([proofOfDelivery]);
        return proofOfDelivery;
    }
}
