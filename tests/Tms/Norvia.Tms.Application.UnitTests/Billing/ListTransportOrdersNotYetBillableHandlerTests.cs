using Ardalis.SharedKernel;
using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Billing;

/// <summary>P9's Customer service list: Delivered Transport-only orders that aren't Billable, each with its blocking reasons, oldest delivery first.</summary>
public sealed class ListTransportOrdersNotYetBillableHandlerTests
{
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly IReadRepository<ExceptionCase> _exceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly List<TransportOrder> _allOrders = [];
    private readonly List<DeliveredShipmentOfOrderRow> _delivered = [];
    private readonly List<DeliveredEventRow> _events = [];
    private readonly List<ShipmentId> _withProof = [];
    private readonly List<ShipmentId> _withException = [];
    private readonly ListTransportOrdersNotYetBillableHandler _handler;

    public ListTransportOrdersNotYetBillableHandlerTests()
    {
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", true));
        _orders.ListAsync(Arg.Any<TransportOrdersNotYetBillableSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allOrders);
        _shipments.ListAsync(Arg.Any<DeliveredShipmentsOfOrdersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _delivered);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _events);
        _proofs.ListAsync(Arg.Any<CurrentProofsOfDeliveryForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _withProof);
        _exceptions.ListAsync(Arg.Any<OpenExceptionsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _withException);
        _handler = new(_orders, _shipments, _trackingEvents, _proofs, _exceptions, _shippers);
    }

    [Fact]
    public async Task Handle_NoOrdersNotYetBillable_ListsNothing()
    {
        (await Handle()).Should().BeEmpty();

        await _shipments.DidNotReceive().ListAsync(Arg.Any<DeliveredShipmentsOfOrdersSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoShipmentIsDelivered_ListsNothing()
    {
        _allOrders.Add(Samples.RatedOrder(sequence: 7));

        (await Handle()).Should().BeEmpty();

        await _proofs.DidNotReceive().ListAsync(Arg.Any<CurrentProofsOfDeliveryForShipmentsSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ARatedDeliveredOrderWithoutProof_ListsItWithShipperReferenceDeliveryTimeAndNoProofOfDelivery()
    {
        Delivered(Samples.RatedOrder(sequence: 7), shipmentSequence: 12, DeliveredAt);

        var summary = (await Handle()).Should().ContainSingle().Subject;

        summary.TransportOrderNumber.Should().Be("TO-0000007");
        summary.ShipmentNumber.Should().Be("SH-0000012");
        summary.Shipper.Should().Be(new ShipperView("SHP-0001", "Brouwer Dranken"));
        summary.ShipperReference.Should().Be("BD-2026-0001");
        summary.DeliveredAt.Should().Be(DeliveredAt);
        summary.BlockingReasons.Should().Equal(BillingBlockingReason.NoProofOfDelivery);
    }

    [Fact]
    public async Task Handle_EachMissingCondition_ShowsItsOwnReason()
    {
        Delivered(Samples.RatingFailedOrder(sequence: 7), shipmentSequence: 12, DeliveredAt);
        var excepted = Delivered(Samples.RatedOrder(sequence: 8), shipmentSequence: 13, DeliveredAt.AddHours(1));
        _withProof.Add(excepted);
        _withException.Add(excepted);
        var everything = Delivered(Samples.RatingFailedOrder(sequence: 9), shipmentSequence: 14, DeliveredAt.AddHours(2));
        _withException.Add(everything);

        var listed = await Handle();

        listed.Should().HaveCount(3);
        listed[0].BlockingReasons.Should().Equal(BillingBlockingReason.NotRated, BillingBlockingReason.NoProofOfDelivery);
        listed[1].BlockingReasons.Should().Equal(BillingBlockingReason.OpenException);
        listed[2].BlockingReasons.Should().Equal(BillingBlockingReason.NotRated, BillingBlockingReason.NoProofOfDelivery, BillingBlockingReason.OpenException);
    }

    [Fact]
    public async Task Handle_ReasonsDisappearWhenTheirConditionIsMet()
    {
        var shipment = Delivered(Samples.RatingFailedOrder(sequence: 7), shipmentSequence: 12, DeliveredAt);
        (await Handle()).Single().BlockingReasons.Should().Contain(BillingBlockingReason.NoProofOfDelivery);

        _withProof.Add(shipment);

        (await Handle()).Single().BlockingReasons.Should().Equal(BillingBlockingReason.NotRated);
    }

    [Fact]
    public async Task Handle_AnOrderReleasedWithoutAProofOfDelivery_NoLongerShowsNoProofOfDelivery()
    {
        var order = Samples.RatingFailedOrder(sequence: 7);
        typeof(TransportOrder).GetProperty(nameof(TransportOrder.ReleasedWithoutProofOfDelivery))!.SetValue(order, true);
        Delivered(order, shipmentSequence: 12, DeliveredAt);

        (await Handle()).Single().BlockingReasons.Should().Equal(BillingBlockingReason.NotRated);
    }

    [Fact]
    public async Task Handle_PutsTheOldestDeliveryFirst_AndAnOrderWhoseShipmentIsNotDeliveredIsLeftOut()
    {
        Delivered(Samples.RatedOrder(sequence: 7), shipmentSequence: 12, DeliveredAt.AddHours(5));
        Delivered(Samples.RatedOrder(sequence: 8), shipmentSequence: 13, DeliveredAt);
        var undelivered = Samples.RatedOrder(sequence: 9);
        Samples.Planned(undelivered, CarriedBy.Carrier(VanDijk), sequence: 14);
        _allOrders.Add(undelivered);

        (await Handle()).Select(s => s.TransportOrderNumber).Should().Equal("TO-0000008", "TO-0000007");
    }

    [Fact]
    public async Task Handle_ADeliveredShipmentWithoutItsDeliveredEvent_FailsRatherThanListingAGuess()
    {
        var order = Samples.RatedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        _allOrders.Add(order);
        _delivered.Add(new(shipment.Id, shipment.Number, order.Number!));

        var act = async () => await Handle();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<IReadOnlyList<TransportOrderNotYetBillableSummary>> Handle() =>
        await _handler.Handle(new ListTransportOrdersNotYetBillableQuery(), CancellationToken.None);

    private ShipmentId Delivered(TransportOrder order, long shipmentSequence, DateTimeOffset deliveredAt)
    {
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), shipmentSequence);
        _allOrders.Add(order);
        _delivered.Add(new(shipment.Id, shipment.Number, order.Number!));
        _events.Add(new(shipment.Id, deliveredAt));
        return shipment.Id;
    }
}
