using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Application.ShipperTracking.ReadProofOfDeliveryFile;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.ShipperTracking;

/// <summary>A Shipper reads a file of its Transport Order's current Proof of delivery, and of no other (P8).</summary>
public sealed class GetShipperProofOfDeliveryFileHandlerTests
{
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IProofOfDeliveryFiles _files = Substitute.For<IProofOfDeliveryFiles>();
    private readonly Shipment _shipment = ShipmentCarriedTo(ShipmentStatus.Delivered);

    public GetShipperProofOfDeliveryFileHandlerTests()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder(sequence: 7));
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([_shipment]);
    }

    [Fact]
    public async Task ACurrentFile_IsStreamed()
    {
        var current = ProofOfDelivery.Record(_shipment, Report(files: [Cmr()]), new CorrelationId("corr-pod"), Samples.Now);
        _proofs.FirstOrDefaultAsync(Arg.Is<CurrentProofOfDeliverySpec>(s => s.ShipmentId == _shipment.Id), Arg.Any<CancellationToken>()).Returns(current);
        using var content = new MemoryStream([1, 2, 3, 4]);
        _files.OpenReadAsync(_shipment.Number, Cmr(), Arg.Any<CancellationToken>()).Returns(content);

        var result = await Handler().Handle(new GetShipperProofOfDeliveryFileQuery("SHP-0001", "TO-0000007", Cmr().Id), CancellationToken.None);

        result.Value.Should().Be(new ProofOfDeliveryFileContent("cmr.pdf", "application/pdf", content));
    }

    [Fact]
    public async Task AFileOfASupersededProofOfDelivery_IsProofOfDeliveryNotFound()
    {
        // The current Proof of delivery holds another file; the superseded one's file is never looked up.
        var current = ProofOfDelivery.Record(_shipment, Report(files: [Cmr() with { Id = Guid.NewGuid() }]), new CorrelationId("corr-pod"), Samples.Now);
        _proofs.FirstOrDefaultAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(current);

        var result = await Handler().Handle(new GetShipperProofOfDeliveryFileQuery("SHP-0001", "TO-0000007", Cmr().Id), CancellationToken.None);

        NotFound(result, "ProofOfDeliveryNotFound");
        await _files.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ATransportOrderWithNoProofOfDelivery_IsProofOfDeliveryNotFound()
    {
        var result = await Handler().Handle(new GetShipperProofOfDeliveryFileQuery("SHP-0001", "TO-0000007", Cmr().Id), CancellationToken.None);

        NotFound(result, "ProofOfDeliveryNotFound");
    }

    [Fact]
    public async Task AFileTheStorageHasLost_IsProofOfDeliveryNotFound()
    {
        var current = ProofOfDelivery.Record(_shipment, Report(files: [Cmr()]), new CorrelationId("corr-pod"), Samples.Now);
        _proofs.FirstOrDefaultAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(current);
        _files.OpenReadAsync(_shipment.Number, Cmr(), Arg.Any<CancellationToken>()).Returns((Stream?)null);

        var result = await Handler().Handle(new GetShipperProofOfDeliveryFileQuery("SHP-0001", "TO-0000007", Cmr().Id), CancellationToken.None);

        NotFound(result, "ProofOfDeliveryNotFound");
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("not-a-number")]
    public async Task AnUnknownTransportOrderOrAnotherShippers_IsTransportOrderNotFound(string number)
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await Handler().Handle(new GetShipperProofOfDeliveryFileQuery("SHP-0003", number, Cmr().Id), CancellationToken.None);

        NotFound(result, "TransportOrderNotFound");
    }

    private static void NotFound(Result<ProofOfDeliveryFileContent> result, string code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be(code);
    }

    private GetShipperProofOfDeliveryFileHandler Handler() => new(_orders, _shipments, _proofs, _files);
}
