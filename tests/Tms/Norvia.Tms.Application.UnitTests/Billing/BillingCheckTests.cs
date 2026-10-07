using Ardalis.SharedKernel;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.Billing;

public sealed class BillingCheckTests
{
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);
    private static readonly BillingChange Change = new(new CorrelationId("corr-billing"), Samples.Now.AddDays(3));

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly BillingCheck _check;

    public BillingCheckTests()
    {
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _check = new BillingCheck(_orders, _shipments, _trackingEvents, _proofs, _exceptions);
    }

    [Fact]
    public async Task Check_ByShipment_LoadsItsTransportOrder_AndMakesItBillableWhenTheChangeCompletedTheLastCondition()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();
        _orders.SingleOrDefaultAsync(Arg.Is<TransportOrderToBillSpec>(s => s.Number == order.Number), Arg.Any<CancellationToken>()).Returns(order);
        _proofs.AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _check.CheckAsync(shipment, Change with { DeliveredAt = DeliveredAt }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.Billable);
        order.DeliveredAt.Should().Be(DeliveredAt);
        order.BillingCorrelationId.Should().Be(new CorrelationId("corr-billing"));
        order.BecameBillableAt.Should().Be(Samples.Now.AddDays(3));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderBillableEvent>();
    }

    [Fact]
    public async Task Check_ByShipment_WithNoTransportOrder_DoesNothing()
    {
        var (_, shipment) = RatedOrderAndDeliveredShipment();

        await _check.CheckAsync(shipment, Change with { DeliveredAt = DeliveredAt }, CancellationToken.None);

        await _proofs.DidNotReceiveWithAnyArgs().AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_AfterRating_FindsTheShipmentItsDeliveryAndItsCurrentProofOfDeliveryItself()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentByNumberSpec>(s => s.Number == order.ShipmentNumber), Arg.Any<CancellationToken>()).Returns(shipment);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([new DeliveredEventRow(shipment.Id, DeliveredAt)]);
        _proofs.AnyAsync(Arg.Is<CurrentProofOfDeliverySpec>(s => s.ShipmentId == shipment.Id), Arg.Any<CancellationToken>()).Returns(true);

        await _check.CheckAsync(order, shipment: null, Change, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.Billable);
        order.DeliveredAt.Should().Be(DeliveredAt);
    }

    [Fact]
    public async Task Check_AfterTheProofOfDeliveryIsRecorded_NeedsNoStoredProofOfDelivery()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([new DeliveredEventRow(shipment.Id, DeliveredAt)]);

        await _check.CheckAsync(order, shipment, Change with { ProofOfDeliveryRecorded = true }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.Billable);
        await _proofs.DidNotReceiveWithAnyArgs().AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_ShipmentNotDelivered_LeavesTheOrderNotYetBillable_WithoutLookingForAProofOfDelivery()
    {
        var order = Samples.RatedOrder(sequence: 7);
        var shipment = Samples.Planned(order, Norvia.Tms.Domain.Shipments.CarriedBy.OwnFleet, sequence: 12);
        order.ClearDomainEvents();

        await _check.CheckAsync(order, shipment, Change, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        await _trackingEvents.DidNotReceiveWithAnyArgs().ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>());
        await _proofs.DidNotReceiveWithAnyArgs().AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_DeliveredShipmentWithoutItsDeliveredEvent_LeavesTheOrderNotYetBillable()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();

        await _check.CheckAsync(order, shipment, Change, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public async Task Check_NoCurrentProofOfDelivery_LeavesTheOrderNotYetBillable()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();

        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_OrderNotRated_LeavesItNotYetBillable_WithoutReadingAnythingElse()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, Norvia.Tms.Domain.Shipments.CarriedBy.OwnFleet, sequence: 12);

        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt, ProofOfDeliveryRecorded = true }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        await _shipments.ReceivedWithAnyArgs(0).SingleOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_OrderWithNoShipment_DoesNothing()
    {
        var order = Samples.RatedOrder(sequence: 7);

        await _check.CheckAsync(order, shipment: null, Change, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        await _shipments.DidNotReceiveWithAnyArgs().SingleOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Check_ShipmentTheTmsCantFind_DoesNothing()
    {
        var (order, _) = RatedOrderAndDeliveredShipment();

        await _check.CheckAsync(order, shipment: null, Change, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public async Task Check_FulfilmentOrder_NeverBecomesBillable()
    {
        var order = Samples.AcceptedOrder(sequence: 7, channel: IntakeChannel.Wms);
        var shipment = Samples.Planned(order, Norvia.Tms.Domain.Shipments.CarriedBy.OwnFleet, sequence: 12);

        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt, ProofOfDeliveryRecorded = true }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotBilledSeparately);
    }

    [Fact]
    public async Task Check_BillableOrder_StaysBillableAndPublishesNothingMore()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();
        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt, ProofOfDeliveryRecorded = true }, CancellationToken.None);
        order.ClearDomainEvents();

        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt.AddDays(1), ProofOfDeliveryRecorded = true }, CancellationToken.None);

        order.DomainEvents.Should().BeEmpty();
        order.DeliveredAt.Should().Be(DeliveredAt);
    }

    [Fact]
    public async Task Check_ShipmentWithAnOpenException_LeavesTheOrderNotYetBillable()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();
        _exceptions.AnyAsync(Arg.Is<OpenExceptionOfShipmentSpec>(s => s != null), Arg.Any<CancellationToken>()).Returns(true);

        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt, ProofOfDeliveryRecorded = true }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_TheTransactionOpenedAnException_LeavesTheOrderNotYetBillable_BeforeTheStoreCanKnow()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();

        await _check.CheckAsync(order, shipment, Change with { DeliveredAt = DeliveredAt, ProofOfDeliveryRecorded = true, ExceptionOpened = true }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public async Task Check_TheTransactionResolvedTheLastOpenException_MakesTheOrderBillable_ForAnyOutcome()
    {
        var (order, shipment) = RatedOrderAndDeliveredShipment();
        var resolving = ExceptionId.New();
        // The store still has the resolved Exception as open; only the other Exceptions count.
        _exceptions.AnyAsync(Arg.Is<OpenExceptionOfShipmentSpec>(s => s != null), Arg.Any<CancellationToken>()).Returns(false);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([new DeliveredEventRow(shipment.Id, DeliveredAt)]);
        _proofs.AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _check.CheckAsync(order, shipment, Change with { ExceptionResolved = resolving }, CancellationToken.None);

        order.BillingState.Should().Be(BillingState.Billable);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderBillableEvent>();
    }

    private static (TransportOrder Order, Shipment Shipment) RatedOrderAndDeliveredShipment()
    {
        var order = Samples.RatedOrder(sequence: 7);
        var shipment = ShipmentCarriedTo(ShipmentStatus.Delivered);
        order.Plan(shipment.Number, Norvia.Tms.Domain.Shipments.CarriedBy.Carrier(VanDijk), new IdempotencyKey("plan"), new CorrelationId("plan-corr"), Samples.Now.AddHours(1));
        order.ClearDomainEvents();
        return (order, shipment);
    }
}
