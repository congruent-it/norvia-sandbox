using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Record;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

public sealed class RecordProofOfDeliveryHandlerTests
{
    private static readonly byte[] CmrContent = [1, 2, 3, 4];

    private readonly IRepository<ProofOfDelivery> _proofsOfDelivery = Substitute.For<IRepository<ProofOfDelivery>>();
    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IProofOfDeliveryFiles _files = Substitute.For<IProofOfDeliveryFiles>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(2));
    private readonly RecordProofOfDeliveryHandler _handler;

    public RecordProofOfDeliveryHandlerTests()
    {
        _handler = new RecordProofOfDeliveryHandler(_proofsOfDelivery, _carrierBookings, _shipments, _files, _time);
    }

    [Fact]
    public async Task Handle_ForADeliveredShipment_RecordsItAsCurrent_KeepsTheFile_AndLeavesTheStatusAlone()
    {
        var shipment = Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShipmentNumber.Should().Be("SH-0000012");
        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Delivered);
        result.Value.DocumentId.Should().Be("doc-1");
        result.Value.NumberOfFiles.Should().Be(1);
        result.Value.CorrelationId.Should().Be("corr-pod");
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        var recorded = (ProofOfDelivery)_proofsOfDelivery.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ProofOfDelivery>.AddAsync)).GetArguments()[0]!;
        recorded.IsCurrent.Should().BeTrue();
        recorded.CorrelationId.Should().Be(new CorrelationId("corr-pod"));
        recorded.ReceivedAt.Should().Be(Samples.Now.AddDays(2));
        recorded.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProofOfDeliveryReceivedEvent>();
        await _files.Received(1).StoreAsync(shipment.Number, Cmr(), Arg.Is<ReadOnlyMemory<byte>>(content => content.ToArray().SequenceEqual(CmrContent)), Arg.Any<CancellationToken>());
        await _proofsOfDelivery.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ShipmentStatus.Booked)]
    [InlineData(ShipmentStatus.Collected)]
    public async Task Handle_ForABookedOrLaterShipment_RecordsIt_WithoutChangingTheStatus(ShipmentStatus status)
    {
        var shipment = Found(status);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(status);
    }

    [Fact]
    public async Task Handle_ForAReferenceNoConfirmedBookingOfTheCarrierHas_IsRefusedWithShipmentNotFound_AndStoresNothing()
    {
        Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(Report(reference: "UNKNOWN")), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_FromOwnFleet_IsRefusedWithShipmentNotFound_ForNowOwnFleetFindsItsShipmentByTrip()
    {
        Found(ShipmentStatus.Delivered);

        var result = await _handler.Handle(Command(Report(ProofOfDeliverySource.OwnFleet)), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_ForAShipmentThatIsNotBookedYet_IsRefusedWithShipmentNotInTransport()
    {
        Found(ShipmentStatus.Released);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        await AssertRefusedAsync(result, ProofOfDeliveryRefusalCode.ShipmentNotInTransport);
    }

    [Fact]
    public async Task Handle_WithoutContentForEveryFile_IsABug()
    {
        Found(ShipmentStatus.Delivered);

        var act = async () => await _handler.Handle(Command() with { Files = [] }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_ForABookingWhoseShipmentIsGone_IsABug()
    {
        var shipment = ShipmentCarriedTo(ShipmentStatus.Delivered);
        _carrierBookings.SingleOrDefaultAsync(Arg.Any<ConfirmedCarrierBookingByReferenceSpec>(), Arg.Any<CancellationToken>()).Returns(ConfirmedBooking(shipment));

        var act = async () => await _handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task AssertRefusedAsync(Result<RecordProofOfDeliveryResult> result, ProofOfDeliveryRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _files.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default, default);
        await _proofsOfDelivery.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    private Shipment Found(ShipmentStatus status)
    {
        var shipment = ShipmentCarriedTo(status);
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByReferenceSpec>(s => s.CarrierBookingReference == "VDT-1" && s.CarrierId == VanDijk), Arg.Any<CancellationToken>())
            .Returns(ConfirmedBooking(shipment));
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>()).Returns(shipment);
        return shipment;
    }

    private static RecordProofOfDeliveryCommand Command(ProofOfDeliveryReport? report = null) =>
        new(report ?? Report(), [CmrContent], "corr-pod");
}
