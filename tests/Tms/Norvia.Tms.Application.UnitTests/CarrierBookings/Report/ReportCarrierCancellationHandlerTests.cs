using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CarrierBookings.Report;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Report;

/// <summary>The Carrier cancellation Logic App reports a Cancelled or Withdrawn Carrier booking's message as told or not delivered (P7.8).</summary>
public sealed class ReportCarrierCancellationHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(5));
    private readonly ReportCarrierCancellationToldHandler _told;
    private readonly ReportCarrierCancellationNotDeliveredHandler _notDelivered;

    public ReportCarrierCancellationHandlerTests()
    {
        _told = new ReportCarrierCancellationToldHandler(_carrierBookings, _time);
        _notDelivered = new ReportCarrierCancellationNotDeliveredHandler(_carrierBookings, _time);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Told_ACancelledOrWithdrawnBooking_RecordsTheToldTime_AndPublishesNothing(bool withdrawn)
    {
        var booking = Given(withdrawn ? Withdrawn() : Cancelled());

        var result = await _told.Handle(new ReportCarrierCancellationToldCommand("CB-0000001", "corr-cancel"), CancellationToken.None);

        result.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", booking.Status));
        booking.CarrierToldAt.Should().Be(Samples.Now.AddHours(5));
        booking.DomainEvents.Should().BeEmpty();
        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotDelivered_ACancelledOrWithdrawnBooking_FlagsCarrierNotTold_AndRaisesCarrierCancellationNotDelivered(bool withdrawn)
    {
        var booking = Given(withdrawn ? Withdrawn() : Cancelled());

        var result = await _notDelivered.Handle(
            new ReportCarrierCancellationNotDeliveredCommand("CB-0000001", "corr-cancel", 503, "Service unavailable."), CancellationToken.None);

        result.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", booking.Status));
        booking.IsCarrierNotTold.Should().BeTrue();
        booking.CarrierNotToldStatusCode.Should().Be(503);
        booking.CarrierNotToldMessage.Should().Be("Service unavailable.");
        booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierCancellationNotDeliveredEvent>();
        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_ABookingAlreadyTold_AreIgnored_ChangingAndPublishingNothing()
    {
        var booking = Given(Cancelled());
        booking.MarkCarrierTold(Samples.Now.AddHours(4));

        var told = await _told.Handle(new ReportCarrierCancellationToldCommand("CB-0000001", "corr-cancel"), CancellationToken.None);
        var notDelivered = await _notDelivered.Handle(
            new ReportCarrierCancellationNotDeliveredCommand("CB-0000001", "corr-cancel", 503, "Service unavailable."), CancellationToken.None);

        told.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", CarrierBookingStatus.Cancelled, Ignored: true));
        notDelivered.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", CarrierBookingStatus.Cancelled, Ignored: true));
        booking.CarrierToldAt.Should().Be(Samples.Now.AddHours(4));
        booking.IsCarrierNotTold.Should().BeFalse();
        booking.DomainEvents.Should().BeEmpty();
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotDelivered_ARepeatReport_IsIgnored_AndPublishesNothingAgain()
    {
        var booking = Given(Cancelled());
        await _notDelivered.Handle(new ReportCarrierCancellationNotDeliveredCommand("CB-0000001", "corr-cancel", 503, "Down"), CancellationToken.None);
        booking.ClearDomainEvents();
        _carrierBookings.ClearReceivedCalls();

        var repeat = await _notDelivered.Handle(new ReportCarrierCancellationNotDeliveredCommand("CB-0000001", "corr-cancel", 500, "Other"), CancellationToken.None);

        repeat.Value.Ignored.Should().BeTrue();
        booking.CarrierNotToldMessage.Should().Be("Down");
        booking.DomainEvents.Should().BeEmpty();
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_UnknownBookingNumber_AreRefused_WithBookingNotFound()
    {
        var told = await _told.Handle(new ReportCarrierCancellationToldCommand("CB-9999999", "corr-cancel"), CancellationToken.None);
        var notDelivered = await _notDelivered.Handle(
            new ReportCarrierCancellationNotDeliveredCommand("CB-9999999", "corr-cancel", 503, "Down"), CancellationToken.None);

        foreach (var result in new[] { told, notDelivered })
        {
            result.Status.Should().Be(ResultStatus.Invalid);
            result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotFound));
        }

        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_ABookingNorviaDidntEnd_AreRefused_WithBookingNotCancelled()
    {
        var booking = Given(CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), ShipmentId.New(), ShipmentNumber.FromSequence(12), VanDijk, new CorrelationId("corr-release"), Samples.Now));

        var told = await _told.Handle(new ReportCarrierCancellationToldCommand("CB-0000001", "corr-cancel"), CancellationToken.None);
        var notDelivered = await _notDelivered.Handle(
            new ReportCarrierCancellationNotDeliveredCommand("CB-0000001", "corr-cancel", 503, "Down"), CancellationToken.None);

        foreach (var result in new[] { told, notDelivered })
        {
            result.Status.Should().Be(ResultStatus.Invalid);
            result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotCancelled));
        }

        booking.CarrierToldAt.Should().BeNull();
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CarrierBooking Given(CarrierBooking booking)
    {
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<CarrierBookingByNumberSpec>(spec => spec.Number == booking.Number), Arg.Any<CancellationToken>())
            .Returns(booking);
        return booking;
    }

    private static CarrierBooking Withdrawn()
    {
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), ShipmentId.New(), ShipmentNumber.FromSequence(12), VanDijk, new CorrelationId("corr-release"), Samples.Now);
        booking.Withdraw(new IdempotencyKey("key-withdraw"), new CorrelationId("corr-cancel"), Samples.Now.AddHours(1));
        booking.ClearDomainEvents();
        return booking;
    }

    private static CarrierBooking Cancelled()
    {
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), ShipmentId.New(), ShipmentNumber.FromSequence(12), VanDijk, new CorrelationId("corr-release"), Samples.Now);
        booking.Confirm("VDT-1", null, TransportOrderNumber.FromSequence(7), new CorrelationId("corr-answer"), Samples.Now.AddHours(1));
        booking.Cancel("No show", ExceptionNumber.FromSequence(5), new IdempotencyKey("key-cancel"), new CorrelationId("corr-cancel"), Samples.Now.AddHours(2));
        booking.ClearDomainEvents();
        return booking;
    }
}
