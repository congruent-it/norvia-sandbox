using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Answer;

public sealed class RejectCarrierBookingHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CarrierBookingNumber BookingNumber = CarrierBookingNumber.FromSequence(1);

    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly RejectCarrierBookingHandler _handler;

    public RejectCarrierBookingHandlerTests()
    {
        _handler = new RejectCarrierBookingHandler(_carrierBookings, _shipments, _time);
    }

    [Fact]
    public async Task Handle_ARequestedBooking_RejectsIt_AndReturnsTheShipmentToPlanned()
    {
        var (booking, shipment) = ReleasedBooking();

        var result = await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer", CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday."),
            CancellationToken.None);

        result.Value.Should().Be(
            new CarrierBookingAnswerResult("CB-0000001", CarrierBookingStatus.Rejected, "SH-0000012", ShipmentStatus.Planned, "corr-answer"));
        booking.RejectionReason.Should().Be(CarrierBookingRejectionReason.NoCapacity);
        booking.RejectionReasonText.Should().Be("No trucks on Friday.");
        booking.AnswerCorrelationId.Should().Be(new CorrelationId("corr-answer"));
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingRejectedEvent>();
    }

    [Fact]
    public async Task Handle_ARequestedBooking_SavesTheChangeInOneSave()
    {
        ReleasedBooking();

        await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        // Rejecting saves the tracked Shipment's move back to Planned with it, in one transaction.
        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var result = await _handler.Handle(
            new RejectCarrierBookingCommand("CB-9999999", "CAR-0001", "corr-answer", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotFound));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnAnswerFromAnotherCarrier_IsRefused_WithNoStableCode_AndChangesNothing()
    {
        var (booking, _) = ReleasedBooking();

        var result = await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-9999", "corr-answer", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().BeEmpty("the wrong-Carrier rule has no stable code, only \"Refused\"");
        booking.Status.Should().Be(CarrierBookingStatus.Requested, "the wrong Carrier's answer changed nothing");
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADifferentRejectionOfABookingAlreadyRejected_IsRefused_WithBookingNotOpen_AndChangesNothing()
    {
        var (booking, shipment) = ReleasedBooking();
        await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-1", CarrierBookingRejectionReason.LaneNotServed, null),
            CancellationToken.None);

        var result = await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-2", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotOpen));
        booking.RejectionReason.Should().Be(CarrierBookingRejectionReason.LaneNotServed, "the later, conflicting answer changed nothing");
        shipment.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public async Task Handle_ARejectionOfABookingAlreadyConfirmed_IsRefused_WithBookingNotOpen()
    {
        var (booking, shipment) = ReleasedBooking();
        booking.Confirm("VDT-240917-0042", null, shipment.TransportOrderNumber, new CorrelationId("corr-answer-1"), Samples.Now.AddHours(2));

        var result = await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-2", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotOpen));
    }

    [Fact]
    public async Task Handle_TheExactSameRejectionRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_WithoutSavingAgain()
    {
        ReleasedBooking();
        await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-1", CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday."),
            CancellationToken.None);
        _carrierBookings.ClearReceivedCalls();

        var result = await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-2", CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday."),
            CancellationToken.None);

        result.Value.Should().Be(
            new CarrierBookingAnswerResult("CB-0000001", CarrierBookingStatus.Rejected, "SH-0000012", ShipmentStatus.Planned, "corr-answer-1", Repeat: true));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private (CarrierBooking Booking, Shipment Shipment) ReleasedBooking()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), BookingNumber);
        shipment.ClearDomainEvents();
        var booking = CarrierBooking.Request(BookingNumber, shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));

        _carrierBookings.SingleOrDefaultAsync(Arg.Is<CarrierBookingByNumberSpec>(s => s.Number == BookingNumber), Arg.Any<CancellationToken>())
            .Returns(booking);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>())
            .Returns(shipment);
        return (booking, shipment);
    }
}
