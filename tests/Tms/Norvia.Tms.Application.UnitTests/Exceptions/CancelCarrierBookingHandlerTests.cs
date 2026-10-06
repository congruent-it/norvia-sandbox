using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.Exceptions.CancelCarrierBooking;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

/// <summary>From an open Exception the Planner cancels a Confirmed Carrier booking while the Shipment is Booked (P7, ADR 0010).</summary>
public sealed class CancelCarrierBookingHandlerTests
{
    private static readonly CarrierId Carrier = new("CAR-0001");

    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(3));
    private readonly CancelCarrierBookingHandler _handler;

    public CancelCarrierBookingHandlerTests() => _handler = new CancelCarrierBookingHandler(_exceptions, _shipments, _carrierBookings, _time);

    [Fact]
    public async Task Handle_ABookedShipmentWithAConfirmedBooking_CancelsIt_SendsTheShipmentBackToPlanned_RaisesTheEvent_AndSavesOnce()
    {
        var (exception, shipment, booking) = Found(OpenExceptions.DeliveryFailed());

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        result.Value.Should().Be(new CancelCarrierBookingResult("EX-0000005", "SH-0000012", "CB-0000042", "corr-cancel"));
        booking.Status.Should().Be(CarrierBookingStatus.Cancelled);
        booking.CancellationReasonText.Should().Be("Carrier won't make its collection");
        booking.CancelledAt.Should().Be(Samples.Now.AddDays(3));
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingCancelledEvent>();
        exception.Status.Should().Be(ExceptionStatus.Open, "cancelling doesn't resolve the Exception");
        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Delivered)]
    public async Task Handle_ACollectedOrLaterShipment_IsRefused_WithShipmentAlreadyCollected(TrackingEventType reached)
    {
        var (_, shipment, booking) = Found(OpenExceptions.DeliveryFailed(), reached);

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ShipmentAlreadyCollected);
        booking.Status.Should().Be(CarrierBookingStatus.Confirmed);
        shipment.Status.Should().NotBe(ShipmentStatus.Planned);
    }

    [Fact]
    public async Task Handle_AShipmentWithoutAConfirmedBooking_IsRefused_WithNoConfirmedCarrierBooking()
    {
        var (_, shipment, booking) = Found(OpenExceptions.DeliveryFailed(), confirmed: false);

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.NoConfirmedCarrierBooking);
        booking.Status.Should().Be(CarrierBookingStatus.Requested);
        shipment.Status.Should().Be(ShipmentStatus.Released);
    }

    [Fact]
    public async Task Handle_AShipmentWhoseBookingWasCancelledAlready_IsRefused_WithNoConfirmedCarrierBooking()
    {
        var (_, _, booking) = Found(OpenExceptions.DeliveryFailed());
        await _handler.Handle(Cancel(), CancellationToken.None);
        _carrierBookings.ClearReceivedCalls();

        var result = await _handler.Handle(Cancel() with { CancellationKey = "key-cancel-2", CorrelationId = "corr-cancel-2" }, CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.NoConfirmedCarrierBooking);
        booking.CancellationUnder(new IdempotencyKey("key-cancel-2")).Should().BeNull();
    }

    [Fact]
    public async Task Handle_AnOwnFleetShipment_IsRefused_WithNoConfirmedCarrierBooking()
    {
        var (_, _, _) = Found(OpenExceptions.DeliveryFailed(), noBookings: true);

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.NoConfirmedCarrierBooking);
    }

    [Fact]
    public async Task Handle_TheSameCancellationSentAgain_IsAnsweredAsBefore_WithoutCancellingOrSavingAgain()
    {
        var (_, _, booking) = Found(OpenExceptions.DeliveryFailed());
        await _handler.Handle(Cancel(), CancellationToken.None);
        booking.ClearDomainEvents();
        _carrierBookings.ClearReceivedCalls();

        var result = await _handler.Handle(Cancel() with { CorrelationId = "corr-retry" }, CancellationToken.None);

        result.Value.Should().Be(new CancelCarrierBookingResult("EX-0000005", "SH-0000012", "CB-0000042", "corr-cancel", Repeat: true));
        booking.DomainEvents.Should().BeEmpty();
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AResolvedException_IsRefused_WithExceptionNotOpen()
    {
        var exception = OpenExceptions.DeliveryFailed();
        exception.Resolve(ExceptionOutcome.NoActionNeeded, "Done", new CorrelationId("corr-1"), Samples.Now.AddDays(2));
        var (_, shipment, booking) = Found(exception);

        var result = await _handler.Handle(Cancel(), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotOpen);
        booking.Status.Should().Be(CarrierBookingStatus.Confirmed);
        shipment.Status.Should().Be(ShipmentStatus.Booked);
    }

    [Theory]
    [InlineData("EX-0000099")]
    [InlineData("not-a-number")]
    public async Task Handle_AnUnknownException_IsRefused_WithExceptionNotFound(string number)
    {
        var result = await _handler.Handle(Cancel(number), CancellationToken.None);

        await AssertRefused(result, ExceptionRefusalCode.ExceptionNotFound);
    }

    private async Task AssertRefused(Result<CancelCarrierBookingResult> result, ExceptionRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private (ExceptionCase Exception, Shipment Shipment, CarrierBooking Booking) Found(
        ExceptionCase exception, TrackingEventType? reached = null, bool confirmed = true, bool noBookings = false)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var carriedBy = noBookings ? CarriedBy.OwnFleet : CarriedBy.Carrier(Carrier);
        var shipment = Samples.Planned(order, carriedBy, sequence: 12);
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(42), shipment.Id, shipment.Number, Carrier, new CorrelationId("corr-release"), Samples.Now.AddHours(3));
        if (!noBookings)
        {
            shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(3), booking.Number);
            if (confirmed)
            {
                booking.Confirm("VDT-1", null, order.Number!, new CorrelationId("corr-answer"), Samples.Now.AddHours(4));
                shipment.Book();
            }
        }

        if (reached is { } type)
        {
            shipment.Record(
                new TrackingReport(TrackingSource.Carrier(Carrier), "evt-1", "VDT-1", null, type, Samples.Now.AddDays(1), new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
                new CorrelationId("corr-tracking"),
                Samples.Now.AddDays(1));
        }

        shipment.ClearDomainEvents();
        booking.ClearDomainEvents();
        _exceptions.SingleOrDefaultAsync(Arg.Is<ExceptionToResolveSpec>(s => s.Number == exception.Number), Arg.Any<CancellationToken>()).Returns(exception);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == exception.ShipmentNumber), Arg.Any<CancellationToken>()).Returns(shipment);
        _carrierBookings.ListAsync(Arg.Is<CarrierBookingsOfShipmentToChangeSpec>(s => s.ShipmentId == shipment.Id), Arg.Any<CancellationToken>())
            .Returns(noBookings ? [] : [booking]);
        return (exception, shipment, booking);
    }

    private static CancelCarrierBookingCommand Cancel(string number = "EX-0000005") =>
        new(number, "Carrier won't make its collection", "key-cancel-1", "corr-cancel");
}
