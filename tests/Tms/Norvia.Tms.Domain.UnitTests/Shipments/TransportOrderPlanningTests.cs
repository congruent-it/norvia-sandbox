using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>A Planner's assignment turns an Accepted Transport Order into its one Planned Shipment (P3, ADR 0007).</summary>
public sealed class TransportOrderPlanningTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PlannedAt = ReceivedAt.AddHours(2);
    private static readonly ShipmentNumber ShipmentNumber = ShipmentNumber.FromSequence(3);
    private static readonly CorrelationId Planning = new("corr-planning");
    private static readonly IdempotencyKey Key = new("key-1");

    [Fact]
    public void Plan_AcceptedOrderToACarrier_CreatesAPlannedShipmentCarriedByTheCarrier()
    {
        var order = Accepted();

        var shipment = order.Plan(ShipmentNumber, CarriedBy.Carrier(new CarrierId("CAR-0001")), Key, Planning, PlannedAt);

        shipment.Should().NotBeNull();
        shipment!.Number.Should().Be(ShipmentNumber);
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        shipment.CarriedBy.Should().Be(CarriedBy.Carrier(new CarrierId("CAR-0001")));
        shipment.CarriedBy.IsOwnFleet.Should().BeFalse();
    }

    [Fact]
    public void Plan_AcceptedOrderToOwnFleet_CreatesAPlannedShipmentCarriedByOwnFleet()
    {
        var shipment = Accepted().Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt);

        shipment!.CarriedBy.Should().Be(CarriedBy.OwnFleet);
        shipment.CarriedBy.IsOwnFleet.Should().BeTrue();
        shipment.CarriedBy.CarrierId.Should().BeNull();
    }

    [Fact]
    public void Plan_AcceptedOrder_ShipmentCarriesTheTransportOrderAndItsShipper()
    {
        var order = Accepted();

        var shipment = order.Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt)!;

        shipment.TransportOrderId.Should().Be(order.Id);
        shipment.TransportOrderNumber.Should().Be(order.Number);
        shipment.ShipperId.Should().Be(new ShipperId("SHP-0001"));
    }

    [Fact]
    public void Plan_AcceptedOrder_ShipmentIsOnThePlanningsOwnTrail()
    {
        var shipment = Accepted().Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt)!;

        shipment.CorrelationId.Should().Be(Planning);
        shipment.PlannedAt.Should().Be(PlannedAt);
    }

    [Fact]
    public void Plan_AcceptedOrder_SetsTheTransportOrderToPlannedWithItsShipmentNumber()
    {
        var order = Accepted();

        order.Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt);

        order.Status.Should().Be(TransportOrderStatus.Planned);
        order.ShipmentNumber.Should().Be(ShipmentNumber);
        order.CorrelationId.Should().Be(new CorrelationId("corr-intake"), "the Transport Order keeps its intake's trail");
    }

    [Fact]
    public void Plan_AcceptedOrder_ShipmentRaisesShipmentPlanned()
    {
        var shipment = Accepted().Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt)!;

        shipment.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ShipmentPlannedEvent>()
            .Which.Shipment.Should().BeSameAs(shipment);
    }

    [Fact]
    public void Plan_WhateverTheRatingResult_PlansTheOrder()
    {
        var order = Accepted();
        order.Rate(null, ReceivedAt.AddSeconds(2));

        var shipment = order.Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt);

        shipment.Should().NotBeNull("the rating result never blocks planning");
        order.RatingResult.Should().Be(RatingResult.RatingFailed);
    }

    [Fact]
    public void Plan_PlannedOrder_ChangesNothing()
    {
        var order = Accepted();
        order.Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt);

        var second = order.Plan(ShipmentNumber.FromSequence(4), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("key-2"), new CorrelationId("corr-2"), PlannedAt);

        second.Should().BeNull("a Transport Order has exactly one Shipment");
        order.ShipmentNumber.Should().Be(ShipmentNumber);
    }

    [Fact]
    public void Plan_RejectedBooking_ChangesNothing()
    {
        var order = TransportOrder.Reject(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            [new RejectionReason(RejectionCode.InactiveShipper, "Inactive.")],
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);

        var shipment = order.Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt);

        shipment.Should().BeNull();
        order.Status.Should().Be(TransportOrderStatus.Rejected);
        order.ShipmentNumber.Should().BeNull();
    }

    [Fact]
    public void WasPlannedUnder_TheKeyItWasPlannedUnder_IsTrue()
    {
        var order = Accepted();
        order.Plan(ShipmentNumber, CarriedBy.OwnFleet, Key, Planning, PlannedAt);

        order.WasPlannedUnder(new IdempotencyKey("key-1")).Should().BeTrue();
        order.WasPlannedUnder(new IdempotencyKey("key-2")).Should().BeFalse();
    }

    [Fact]
    public void WasPlannedUnder_AnOrderNotYetPlanned_IsFalse()
    {
        Accepted().WasPlannedUnder(Key).Should().BeFalse();
    }

    private static TransportOrder Accepted()
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        order.ClearDomainEvents();
        return order;
    }
}
