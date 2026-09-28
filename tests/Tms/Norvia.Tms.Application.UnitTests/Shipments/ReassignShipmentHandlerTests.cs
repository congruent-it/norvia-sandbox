using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.Reassign;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class ReassignShipmentHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CarrierId Kessler = new("CAR-0002");

    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ReassignShipmentHandler _handler;

    public ReassignShipmentHandlerTests()
    {
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _carriers.Find(Kessler).Returns(new Carrier(Kessler, "Kessler Spedition", true));
        _handler = new ReassignShipmentHandler(_shipments, _carriers, _time);
    }

    [Fact]
    public async Task Handle_PlannedShipmentToAnotherCarrier_ReassignsIt_AndSaysWhoCarriedItBefore()
    {
        var shipment = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReassignShipmentCommand("SH-0000012", "CAR-0002", "key-r1", "corr-reassign"), CancellationToken.None);

        result.Value.Should().Be(new ReassignShipmentResult(
            "SH-0000012",
            "TO-0000007",
            ShipmentStatus.Planned,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0002", "Kessler Spedition")),
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-reassign"));
        shipment.CarriedBy.Should().Be(CarriedBy.Carrier(Kessler));
        shipment.Assignments[^1].Should().Be(new ShipmentAssignment(
            CarriedBy.Carrier(Kessler), new("corr-reassign"), Samples.Now.AddHours(3), new("key-r1")));
    }

    [Fact]
    public async Task Handle_PlannedShipmentToOwnFleet_ReassignsItToOwnFleet()
    {
        var shipment = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReassignShipmentCommand("SH-0000012", CarrierId: null, "key-r1", "corr-reassign"), CancellationToken.None);

        result.Value.CarriedBy.Should().Be(new CarriedByView(AssignedTo.OwnFleet, null));
        shipment.CarriedBy.Should().Be(CarriedBy.OwnFleet);
    }

    [Fact]
    public async Task Handle_PlannedShipment_SavesTheReassignmentInOneSave()
    {
        PlannedShipment(CarriedBy.OwnFleet);

        await _handler.Handle(new ReassignShipmentCommand("SH-0000012", "CAR-0001", "key-r1", "corr-reassign"), CancellationToken.None);

        // The save stores the Shipment's change and ShipmentReassigned's outbox message in one transaction.
        await _shipments.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameReassignmentSentAgain_AnswersAsBeforeWithoutReassigningAgain()
    {
        var shipment = PlannedShipment(CarriedBy.Carrier(VanDijk));
        shipment.Reassign(CarriedBy.Carrier(Kessler), new("key-r1"), new("corr-reassign"), Samples.Now.AddHours(3));
        shipment.Reassign(CarriedBy.OwnFleet, new("key-r2"), new("corr-reassign-2"), Samples.Now.AddHours(4));
        shipment.ClearDomainEvents();

        var result = await _handler.Handle(new ReassignShipmentCommand("SH-0000012", "CAR-0002", "key-r1", "corr-retry"), CancellationToken.None);

        result.Value.Should().Be(new ReassignShipmentResult(
            "SH-0000012",
            "TO-0000007",
            ShipmentStatus.Planned,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0002", "Kessler Spedition")),
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-reassign",
            Repeat: true));
        shipment.Assignments.Should().HaveCount(3);
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("SH-0000099")]
    [InlineData("TO-0000007")]
    public async Task Handle_UnknownOrMalformedNumber_IsRefusedAsNotFound(string number)
    {
        var result = await _handler.Handle(new ReassignShipmentCommand(number, null, "key-r1", "corr-reassign"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        var refusal = result.ValidationErrors.Should().ContainSingle().Subject;
        refusal.ErrorCode.Should().Be(nameof(AssignmentRefusalCode.ShipmentNotFound));
        refusal.ErrorMessage.Should().Contain(number);
    }

    [Theory]
    [InlineData("CAR-9999", nameof(AssignmentRefusalCode.UnknownCarrier))]
    [InlineData("CAR-0009", nameof(AssignmentRefusalCode.InactiveCarrier))]
    [InlineData("CAR-0001", nameof(AssignmentRefusalCode.AssignmentUnchanged))]
    public async Task Handle_ACarrierThatCantBeChosen_IsRefusedWithItsCode_AndChangesNothing(string carrierId, string code)
    {
        _carriers.Find(new CarrierId("CAR-0009")).Returns(new Carrier(new CarrierId("CAR-0009"), "Gesloten Vracht", false));
        var shipment = PlannedShipment(CarriedBy.Carrier(VanDijk));

        var result = await _handler.Handle(new ReassignShipmentCommand("SH-0000012", carrierId, "key-r1", "corr-reassign"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code);
        shipment.CarriedBy.Should().Be(CarriedBy.Carrier(VanDijk));
        shipment.Assignments.Should().ContainSingle();
        shipment.DomainEvents.Should().BeEmpty();
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private Shipment PlannedShipment(CarriedBy carriedBy)
    {
        var shipment = Samples.Planned(Samples.AcceptedOrder(sequence: 7), carriedBy, sequence: 12);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToReassignSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>())
            .Returns(shipment);
        return shipment;
    }
}
