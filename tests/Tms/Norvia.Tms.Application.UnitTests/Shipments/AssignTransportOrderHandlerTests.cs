using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders.Assign;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

public sealed class AssignTransportOrderHandlerTests
{
    private readonly IRepository<TransportOrder> _transportOrders = Substitute.For<IRepository<TransportOrder>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IShipmentNumbers _shipmentNumbers = Substitute.For<IShipmentNumbers>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly AssignTransportOrderHandler _handler;

    public AssignTransportOrderHandlerTests()
    {
        _shipmentNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ShipmentNumber.FromSequence(12));
        _carriers.Find(new CarrierId("CAR-0001")).Returns(new Carrier(new CarrierId("CAR-0001"), "Van Dijk Transport", true));
        _handler = new AssignTransportOrderHandler(_transportOrders, _shipments, _shipmentNumbers, _carriers, _time);
    }

    [Fact]
    public async Task Handle_AcceptedOrderToACarrier_PlansItsShipmentWithTheCarrier()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _transportOrders.SingleOrDefaultAsync(Arg.Is<TransportOrderToPlanSpec>(s => s.Number.Value == "TO-0000007"), Arg.Any<CancellationToken>())
            .Returns(order);

        var result = await _handler.Handle(new AssignTransportOrderCommand("TO-0000007", "CAR-0001", "key-1", "corr-planning"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ShipmentNumber.Should().Be("SH-0000012");
        result.Value.TransportOrderNumber.Should().Be("TO-0000007");
        result.Value.Status.Should().Be(ShipmentStatus.Planned);
        result.Value.CarriedBy.Should().Be(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")));
        result.Value.ShipperId.Should().Be("SHP-0001");
        result.Value.Repeat.Should().BeFalse();
        order.Status.Should().Be(TransportOrderStatus.Planned);
        order.ShipmentNumber.Should().Be(ShipmentNumber.FromSequence(12));
    }

    [Fact]
    public async Task Handle_AcceptedOrderToOwnFleet_PlansItsShipmentWithOwnFleet()
    {
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderToPlanSpec>(), Arg.Any<CancellationToken>()).Returns(Samples.AcceptedOrder());

        var result = await _handler.Handle(new AssignTransportOrderCommand("TO-0000007", CarrierId: null, "key-1", "corr-planning"), CancellationToken.None);

        result.Value.CarriedBy.Should().Be(new CarriedByView(AssignedTo.OwnFleet, null));
    }

    [Fact]
    public async Task Handle_AcceptedOrder_StoresTheShipmentWithTheOrdersNewStatusInOneSave()
    {
        var order = Samples.AcceptedOrder();
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderToPlanSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await _handler.Handle(new AssignTransportOrderCommand("TO-0000007", "CAR-0001", "key-1", "corr-planning"), CancellationToken.None);

        // Adding the Shipment saves the tracked Transport Order with it, and ShipmentPlanned's outbox message too.
        await _shipments.Received(1).AddAsync(
            Arg.Is<Shipment>(s => s.Number == ShipmentNumber.FromSequence(12) && s.TransportOrderId == order.Id),
            Arg.Any<CancellationToken>());
        await _transportOrders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AcceptedOrder_PlansTheShipmentOnTheAssignmentsOwnTrail()
    {
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderToPlanSpec>(), Arg.Any<CancellationToken>())
            .Returns(Samples.AcceptedOrder(correlationId: "corr-intake"));
        Shipment? planned = null;
        await _shipments.AddAsync(Arg.Do<Shipment>(s => planned = s), Arg.Any<CancellationToken>());

        var result = await _handler.Handle(new AssignTransportOrderCommand("TO-0000007", "CAR-0001", "key-1", "corr-planning"), CancellationToken.None);

        result.Value.CorrelationId.Should().Be("corr-planning");
        planned!.CorrelationId.Should().Be(new CorrelationId("corr-planning"));
        planned.PlannedAt.Should().Be(Samples.Now.AddHours(3));
    }

    [Fact]
    public async Task Handle_TheSameAssignmentSentAgain_AnswersAsBeforeWithoutPlanningAgain()
    {
        var order = Samples.AcceptedOrder();
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 4, correlationId: "corr-planning", key: "key-1");
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderToPlanSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>()).Returns(shipment);

        var result = await _handler.Handle(new AssignTransportOrderCommand("TO-0000007", "CAR-0001", "key-1", "corr-retry"), CancellationToken.None);

        result.Value.Should().Be(new AssignTransportOrderResult(
            "SH-0000004",
            "TO-0000007",
            ShipmentStatus.Planned,
            new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")),
            "SHP-0001",
            "corr-planning",
            Repeat: true));
        await _shipments.DidNotReceive().AddAsync(Arg.Any<Shipment>(), Arg.Any<CancellationToken>());
        await _shipmentNumbers.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PlannedOrderUnderAnotherKey_IsAConflictAndStoresNothing()
    {
        var order = Samples.AcceptedOrder();
        Samples.Planned(order, CarriedBy.OwnFleet, key: "key-1");
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderToPlanSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(new AssignTransportOrderCommand("TO-0000007", "CAR-0001", "key-2", "corr-other"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Conflict);
        await _shipments.DidNotReceive().AddAsync(Arg.Any<Shipment>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("42")]
    public async Task Handle_UnknownOrMalformedNumber_ReturnsNotFound(string number)
    {
        var result = await _handler.Handle(new AssignTransportOrderCommand(number, null, "key-1", "corr-planning"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
