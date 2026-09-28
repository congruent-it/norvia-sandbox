using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.Release;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class ReleaseShipmentHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IRepository<TransportOrder> _transportOrders = Substitute.For<IRepository<TransportOrder>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ReleaseShipmentHandler _handler;

    public ReleaseShipmentHandlerTests()
    {
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _handler = new ReleaseShipmentHandler(_shipments, _transportOrders, _carriers, _time);
    }

    [Fact]
    public async Task Handle_PlannedCarrierShipment_ReleasesIt_AndSaysWhoCarriesIt()
    {
        var (order, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        result.Value.Should().Be(new ReleaseShipmentResult(
            "SH-0000012",
            "TO-0000007",
            ShipmentStatus.Released,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-release"));
        shipment.Status.Should().Be(ShipmentStatus.Released);
        shipment.LastRelease.Should().Be(new ShipmentRelease(new("corr-release"), Samples.Now.AddHours(3), new("key-rel1")));
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentReleasedEvent>()
            .Which.TransportOrder.Should().BeSameAs(order);
    }

    [Fact]
    public async Task Handle_PlannedShipment_SavesTheReleaseInOneSave()
    {
        PlannedShipment(CarriedBy.OwnFleet);

        await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        // The save stores the Shipment's change and ShipmentReleased's outbox message in one transaction.
        await _shipments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameReleaseSentAgain_AnswersAsBeforeWithoutReleasingAgain()
    {
        var (order, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));
        shipment.Release(order, new("key-rel1"), new("corr-release"), Samples.Now.AddHours(3));
        shipment.ClearDomainEvents();

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-retry"), CancellationToken.None);

        result.Value.Should().Be(new ReleaseShipmentResult(
            "SH-0000012",
            "TO-0000007",
            ShipmentStatus.Released,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-release",
            Repeat: true));
        shipment.DomainEvents.Should().BeEmpty();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReleasedShipmentUnderAnotherKey_IsRefusedAsNotPlanned_AndChangesNothing()
    {
        var (order, shipment) = PlannedShipment(CarriedBy.Carrier(VanDijk));
        shipment.Release(order, new("key-rel1"), new("corr-release"), Samples.Now.AddHours(3));
        shipment.ClearDomainEvents();

        var result = await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel2", "corr-release-2"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        var refusal = result.ValidationErrors.Should().ContainSingle().Subject;
        refusal.ErrorCode.Should().Be(nameof(AssignmentRefusalCode.ShipmentNotPlanned));
        refusal.ErrorMessage.Should().Contain("SH-0000012").And.Contain("released");
        shipment.LastRelease!.CorrelationId.Value.Should().Be("corr-release");
        shipment.DomainEvents.Should().BeEmpty();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SH-0000099")]
    [InlineData("TO-0000007")]
    public async Task Handle_UnknownOrMalformedNumber_IsRefusedAsNotFound(string number)
    {
        var result = await _handler.Handle(new ReleaseShipmentCommand(number, "key-rel1", "corr-release"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        var refusal = result.ValidationErrors.Should().ContainSingle().Subject;
        refusal.ErrorCode.Should().Be(nameof(AssignmentRefusalCode.ShipmentNotFound));
        refusal.ErrorMessage.Should().Contain(number);
    }

    [Fact]
    public async Task Handle_ShipmentWhoseTransportOrderIsMissing_Throws()
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), CarriedBy.OwnFleet, sequence: 12);
        _shipments.SingleOrDefaultAsync(Arg.Any<ShipmentToChangeSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);

        var release = async () => await _handler.Handle(new ReleaseShipmentCommand("SH-0000012", "key-rel1", "corr-release"), CancellationToken.None);

        await release.Should().ThrowAsync<InvalidOperationException>();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private (TransportOrder Order, Shipment Shipment) PlannedShipment(CarriedBy carriedBy)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, carriedBy, sequence: 12);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>())
            .Returns(shipment);
        _transportOrders.SingleOrDefaultAsync(Arg.Is<TransportOrderByNumberSpec>(s => s.Number == order.Number), Arg.Any<CancellationToken>())
            .Returns(order);
        return (order, shipment);
    }
}
