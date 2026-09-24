using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.Shippers;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.TransportOrders.Submit;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TransportOrders;

public sealed class SubmitTransportOrderHandlerTests
{
    private readonly IRepository<TransportOrder> _repository = Substitute.For<IRepository<TransportOrder>>();
    private readonly ITransportOrderNumbers _numbers = Substitute.For<ITransportOrderNumbers>();
    private readonly IShipperDirectory _shippers = Substitute.For<IShipperDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly SubmitTransportOrderHandler _handler;

    public SubmitTransportOrderHandlerTests()
    {
        _numbers.NextAsync(Arg.Any<CancellationToken>()).Returns(TransportOrderNumber.FromSequence(42));
        _shippers.Find(new ShipperId("SHP-0001")).Returns(new Shipper(new ShipperId("SHP-0001"), "Brouwer Dranken", Active: true));
        _handler = new SubmitTransportOrderHandler(_repository, _numbers, _shippers, new TransportOrderRules(24_000m), _time);
    }

    [Fact]
    public async Task Handle_ValidBooking_ReturnsAcceptedWithTheNextNumber()
    {
        var result = await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TransportOrderStatus.Accepted);
        result.Value.TransportOrderNumber.Should().Be("TO-0000042");
        result.Value.RejectionReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ValidBooking_StoresTheAcceptedTransportOrder()
    {
        await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-1"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<TransportOrder>(order =>
                order.Number!.Value == "TO-0000042"
                && order.Status == TransportOrderStatus.Accepted
                && order.ShipperId.Value == "SHP-0001"
                && order.CorrelationId.Value == "corr-1"
                && order.ReceivedAt == Samples.Now
                && order.Details == Samples.CanonicalTransportOrder().ToDetails()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BookingBreakingRules_ReturnsRejectedWithEveryReason()
    {
        var result = await _handler.Handle(new SubmitTransportOrderCommand(BrokenBooking(), "corr-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TransportOrderStatus.Rejected);
        result.Value.TransportOrderNumber.Should().BeNull();
        result.Value.RejectionReasons.Select(reason => reason.Code).Should().Equal("ShipperReferenceMissing", "GoodsLinesMissing");
        result.Value.RejectionReasons[1].Message.Should().Be("The booking has no goods lines.");
    }

    [Fact]
    public async Task Handle_BookingBreakingRules_StoresTheRejectedBookingWithoutTakingANumber()
    {
        var result = await _handler.Handle(new SubmitTransportOrderCommand(BrokenBooking(), "corr-1"), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<TransportOrder>(order =>
                order.Id.Value == result.Value.Id
                && order.Status == TransportOrderStatus.Rejected
                && order.Number == null
                && order.RejectionReasons.Count == 2
                && order.CorrelationId.Value == "corr-1"
                && order.Details == BrokenBooking().ToDetails()),
            Arg.Any<CancellationToken>());
        await _numbers.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShipperNotInTheShipperData_IsRejectedAsUnknownShipper()
    {
        var booking = Samples.CanonicalTransportOrder() with { ShipperId = "SHP-9999" };

        var result = await _handler.Handle(new SubmitTransportOrderCommand(booking, "corr-1"), CancellationToken.None);

        result.Value.Status.Should().Be(TransportOrderStatus.Rejected);
        result.Value.RejectionReasons.Select(reason => reason.Code).Should().Equal("UnknownShipper");
    }

    [Fact]
    public async Task Handle_ValidBooking_IsNotARepeat()
    {
        var result = await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-1"), CancellationToken.None);

        result.Value.Repeat.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SameBookingAgain_ReturnsTheOriginalTransportOrderFlaggedAsARepeat()
    {
        var original = Samples.AcceptedOrder(sequence: 7);
        HeldBy(original);

        var result = await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-again"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Repeat.Should().BeTrue();
        result.Value.Id.Should().Be(original.Id.Value);
        result.Value.Status.Should().Be(TransportOrderStatus.Accepted);
        result.Value.TransportOrderNumber.Should().Be("TO-0000007");
        result.Value.RejectionReasons.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SameBookingAgain_StoresNothingAndTakesNoNumber()
    {
        HeldBy(Samples.AcceptedOrder(sequence: 7));

        await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-again"), CancellationToken.None);

        await _repository.DidNotReceive().AddAsync(Arg.Any<TransportOrder>(), Arg.Any<CancellationToken>());
        await _numbers.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SameBookingAgainAfterTheCollectionWindowStarted_IsStillARepeat()
    {
        HeldBy(Samples.AcceptedOrder(sequence: 7));
        _time.Advance(TimeSpan.FromDays(3));

        var result = await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-again"), CancellationToken.None);

        result.Value.Repeat.Should().BeTrue("a repeat is recognised before the booking is validated again");
    }

    [Fact]
    public async Task Handle_ReferenceHeldForDifferentContent_IsRejectedAsReused()
    {
        HeldBy(Samples.AcceptedOrder(sequence: 7));
        var booking = Samples.CanonicalTransportOrder() with { Instructions = "Call ahead" };

        var result = await _handler.Handle(new SubmitTransportOrderCommand(booking, "corr-reused"), CancellationToken.None);

        result.Value.Repeat.Should().BeFalse();
        result.Value.Status.Should().Be(TransportOrderStatus.Rejected);
        result.Value.RejectionReasons.Select(reason => reason.Code).Should().Equal("ShipperReferenceReused");
        await _repository.Received(1).AddAsync(
            Arg.Is<TransportOrder>(order => order.Status == TransportOrderStatus.Rejected && order.CorrelationId.Value == "corr-reused"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReferenceHeldByTheSameShipper_IsLookedUpForThatShipperAndReference()
    {
        await _handler.Handle(new SubmitTransportOrderCommand(Samples.CanonicalTransportOrder(), "corr-1"), CancellationToken.None);

        await _repository.Received(1).SingleOrDefaultAsync(
            Arg.Is<TransportOrderByShipperReferenceSpec>(spec => spec.ShipperId == new ShipperId("SHP-0001") && spec.ShipperReference == "BD-2026-0001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoShipperReference_LooksNothingUp()
    {
        await _handler.Handle(new SubmitTransportOrderCommand(BrokenBooking(), "corr-1"), CancellationToken.None);

        await _repository.DidNotReceive().SingleOrDefaultAsync(Arg.Any<TransportOrderByShipperReferenceSpec>(), Arg.Any<CancellationToken>());
    }

    private void HeldBy(TransportOrder original) =>
        _repository.SingleOrDefaultAsync(Arg.Any<TransportOrderByShipperReferenceSpec>(), Arg.Any<CancellationToken>()).Returns(original);

    private static CanonicalTransportOrder BrokenBooking() =>
        Samples.CanonicalTransportOrder() with { ShipperReference = null, GoodsLines = [] };
}
