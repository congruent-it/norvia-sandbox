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

public sealed class ConfirmCarrierBookingHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly CarrierBookingNumber BookingNumber = CarrierBookingNumber.FromSequence(1);

    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ConfirmCarrierBookingHandler _handler;

    public ConfirmCarrierBookingHandlerTests()
    {
        _handler = new ConfirmCarrierBookingHandler(_carrierBookings, _shipments, _time);
    }

    [Fact]
    public async Task Handle_ARequestedBooking_ConfirmsIt_AndBooksTheShipment()
    {
        var (booking, shipment) = ReleasedBooking();

        var result = await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer", "VDT-240917-0042", PlannedCollectionTime: null),
            CancellationToken.None);

        result.Value.Should().Be(
            new CarrierBookingAnswerResult("CB-0000001", CarrierBookingStatus.Confirmed, "SH-0000012", ShipmentStatus.Booked, "corr-answer"));
        booking.CarrierBookingReference.Should().Be("VDT-240917-0042");
        booking.AnswerCorrelationId.Should().Be(new CorrelationId("corr-answer"));
        booking.AnsweredAt.Should().Be(Samples.Now.AddHours(3));
        shipment.Status.Should().Be(ShipmentStatus.Booked);
        booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingConfirmedEvent>()
            .Which.TransportOrderNumber.Should().Be(shipment.TransportOrderNumber);
    }

    [Fact]
    public async Task Handle_ARequestedBooking_SavesTheChangeInOneSave()
    {
        ReleasedBooking();

        await _handler.Handle(new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer", "VDT-240917-0042", null), CancellationToken.None);

        // Confirming saves the tracked Shipment's move to Booked with it, in one transaction.
        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var result = await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-9999999", "CAR-0001", "corr-answer", "VDT-240917-0042", null), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotFound));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnAnswerFromAnotherCarrier_IsRefused_WithNoStableCode_AndChangesNothing()
    {
        var (booking, _) = ReleasedBooking();

        var result = await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-0000001", "CAR-9999", "corr-answer", "VDT-240917-0042", null), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().BeEmpty("the wrong-Carrier rule has no stable code, only \"Refused\"");
        booking.Status.Should().Be(CarrierBookingStatus.Requested, "the wrong Carrier's answer changed nothing");
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADifferentConfirmationOfABookingAlreadyConfirmed_IsRefused_WithBookingNotOpen_AndChangesNothing()
    {
        var (booking, shipment) = ReleasedBooking();
        await _handler.Handle(new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-1", "VDT-240917-0041", null), CancellationToken.None);

        var result = await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-2", "VDT-240917-0042", null), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotOpen));
        booking.CarrierBookingReference.Should().Be("VDT-240917-0041", "the later, conflicting answer changed nothing");
        shipment.Status.Should().Be(ShipmentStatus.Booked);
    }

    [Fact]
    public async Task Handle_AConfirmationOfABookingAlreadyRejected_IsRefused_WithBookingNotOpen()
    {
        var (booking, _) = ReleasedBooking();
        booking.Reject(CarrierBookingRejectionReason.NoCapacity, reasonText: null, new CorrelationId("corr-answer-1"), Samples.Now.AddHours(2));

        var result = await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-2", "VDT-240917-0042", null), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotOpen));
    }

    [Fact]
    public async Task Handle_TheExactSameConfirmationRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_WithoutSavingAgain()
    {
        var plannedCollectionTime = Samples.Now.AddDays(1);
        ReleasedBooking();
        await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-1", "VDT-240917-0042", plannedCollectionTime), CancellationToken.None);
        _carrierBookings.ClearReceivedCalls();

        var result = await _handler.Handle(
            new ConfirmCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer-2", "VDT-240917-0042", plannedCollectionTime), CancellationToken.None);

        result.Value.Should().Be(
            new CarrierBookingAnswerResult("CB-0000001", CarrierBookingStatus.Confirmed, "SH-0000012", ShipmentStatus.Booked, "corr-answer-1", Repeat: true));
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
