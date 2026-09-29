using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CarrierBookings.Withdraw;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Withdraw;

/// <summary>The Planner withdraws a Requested Carrier booking, from the TMS UI (P4.4).</summary>
public sealed class WithdrawCarrierBookingHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly WithdrawCarrierBookingHandler _handler;

    public WithdrawCarrierBookingHandlerTests()
    {
        _handler = new WithdrawCarrierBookingHandler(_carrierBookings, _shipments, _time);
    }

    [Fact]
    public async Task Handle_ARequestedBooking_WithdrawsIt_AndReturnsTheShipmentToPlanned()
    {
        var (booking, shipment) = ReleasedBooking();

        var result = await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-1", "corr-withdraw"), CancellationToken.None);

        result.Value.Should().Be(
            new CarrierBookingWithdrawResult("CB-0000001", CarrierBookingStatus.Withdrawn, "SH-0000012", ShipmentStatus.Planned, "corr-withdraw"));
        booking.WithdrawnAt.Should().Be(Samples.Now.AddHours(3));
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingWithdrawnEvent>();
    }

    [Fact]
    public async Task Handle_ARequestedBooking_SavesTheChangeInOneSave()
    {
        ReleasedBooking();

        await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-1", "corr-withdraw"), CancellationToken.None);

        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var result = await _handler.Handle(new WithdrawCarrierBookingCommand("CB-9999999", "key-withdraw-1", "corr-withdraw"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotFound));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ABookingAlreadyAnsweredDifferently_IsRefused_WithBookingNotOpen_AndChangesNothing()
    {
        var (booking, shipment) = ReleasedBooking();
        booking.Confirm("VDT-240917-0042", null, shipment.TransportOrderNumber, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        booking.ClearDomainEvents();

        var result = await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-1", "corr-withdraw"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotOpen));
        booking.Status.Should().Be(CarrierBookingStatus.Confirmed, "the withdrawal changed nothing");
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheExactSameWithdrawalRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_WithoutWithdrawingAgain()
    {
        ReleasedBooking();
        await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-1", "corr-withdraw-1"), CancellationToken.None);
        _carrierBookings.ClearReceivedCalls();

        var result = await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-1", "corr-withdraw-2"), CancellationToken.None);

        result.Value.Should().Be(
            new CarrierBookingWithdrawResult(
                "CB-0000001", CarrierBookingStatus.Withdrawn, "SH-0000012", ShipmentStatus.Planned, "corr-withdraw-1", Repeat: true));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AWithdrawalUnderAnotherKeyOfAnAlreadyWithdrawnBooking_IsRefused_WithBookingNotOpen()
    {
        ReleasedBooking();
        await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-1", "corr-withdraw-1"), CancellationToken.None);

        var result = await _handler.Handle(new WithdrawCarrierBookingCommand("CB-0000001", "key-withdraw-2", "corr-withdraw-2"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotOpen));
    }

    private (CarrierBooking Booking, Shipment Shipment) ReleasedBooking()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), CarrierBookingNumber.FromSequence(1));
        shipment.ClearDomainEvents();
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));

        _carrierBookings.SingleOrDefaultAsync(Arg.Is<CarrierBookingByNumberSpec>(spec => spec.Number == booking.Number), Arg.Any<CancellationToken>())
            .Returns(booking);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(spec => spec.Number == shipment.Number), Arg.Any<CancellationToken>())
            .Returns(shipment);
        return (booking, shipment);
    }
}
