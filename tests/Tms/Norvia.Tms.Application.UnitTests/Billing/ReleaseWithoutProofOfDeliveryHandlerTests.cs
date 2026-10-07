using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Billing.Release;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.ProofsOfDelivery.ProofsOfDeliverySamples;

namespace Norvia.Tms.Application.UnitTests.Billing;

public sealed class ReleaseWithoutProofOfDeliveryHandlerTests
{
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);

    private readonly IRepository<TransportOrder> _orders = Substitute.For<IRepository<TransportOrder>>();
    private readonly IReadRepository<TransportOrder> _readOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly ReleaseWithoutProofOfDeliveryHandler _handler;

    public ReleaseWithoutProofOfDeliveryHandlerTests()
    {
        _handler = new ReleaseWithoutProofOfDeliveryHandler(_orders, new BillingCheck(_readOrders, _shipments, _trackingEvents, _proofs, _exceptions), _time);
    }

    [Fact]
    public async Task Handle_RatedDeliveredOrderWithoutAProof_RecordsTheReleaseAndMakesItBillableInOneSave()
    {
        var (order, shipment) = OrderAndShipment(delivered: true);

        var result = await _handler.Handle(Command(order), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.BillingState.Should().Be(BillingState.Billable);
        result.Value.ShipmentNumber.Should().Be(shipment.Number.Value);
        order.ReleasedBy.Should().Be("Jan");
        order.ReleaseRemark.Should().Be("CMR by post");
        order.ReleasedAt.Should().Be(_time.GetUtcNow());
        order.BillingCorrelationId.Should().Be(new CorrelationId("corr-release"));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderBillableEvent>();
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderWithAnOpenException_RecordsTheReleaseButStaysNotYetBillable()
    {
        var (order, _) = OrderAndShipment(delivered: true);
        _exceptions.AnyAsync(Arg.Any<OpenExceptionOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(Command(order), CancellationToken.None);

        result.Value.BillingState.Should().Be(BillingState.NotYetBillable);
        order.ReleasedWithoutProofOfDelivery.Should().BeTrue();
        order.DomainEvents.Should().BeEmpty();
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShipmentNotDelivered_IsRefusedWithoutSaving()
    {
        var (order, _) = OrderAndShipment(delivered: false);

        var result = await _handler.Handle(Command(order), CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be("ShipmentNotDelivered");
        order.ReleasedWithoutProofOfDelivery.Should().BeFalse();
        await _orders.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderWithNoShipmentYet_IsRefusedAsNotDelivered()
    {
        var order = Samples.RatedOrder(sequence: 7);
        _orders.SingleOrDefaultAsync(Arg.Any<TransportOrderToBillSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await _handler.Handle(Command(order), CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be("ShipmentNotDelivered");
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("TO-0000099")]
    public async Task Handle_UnknownTransportOrder_IsRefusedAsNotFound(string number)
    {
        var result = await _handler.Handle(new ReleaseWithoutProofOfDeliveryCommand(number, "CMR by post", "Jan", "corr-release"), CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be("TransportOrderNotFound");
        await _orders.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static ReleaseWithoutProofOfDeliveryCommand Command(TransportOrder order) =>
        new(order.Number!.Value, "CMR by post", "Jan", "corr-release");

    private (TransportOrder Order, Shipment Shipment) OrderAndShipment(bool delivered)
    {
        var order = Samples.RatedOrder(sequence: 7);
        var shipment = ShipmentCarriedTo(delivered ? ShipmentStatus.Delivered : ShipmentStatus.Booked);
        order.Plan(shipment.Number, Norvia.Tms.Domain.Shipments.CarriedBy.Carrier(VanDijk), new IdempotencyKey("plan"), new CorrelationId("plan-corr"), Samples.Now.AddHours(1));
        order.ClearDomainEvents();
        _orders.SingleOrDefaultAsync(Arg.Any<TransportOrderToBillSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _shipments.SingleOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        if (delivered)
        {
            _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
                .Returns([new DeliveredEventRow(shipment.Id, DeliveredAt)]);
        }

        return (order, shipment);
    }
}
