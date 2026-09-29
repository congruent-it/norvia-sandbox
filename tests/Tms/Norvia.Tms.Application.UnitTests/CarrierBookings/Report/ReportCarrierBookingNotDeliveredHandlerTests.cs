using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.CarrierBookings.Report;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Report;

/// <summary>The P4 booking Logic App reports a Requested Carrier booking as not delivered, with its technical cause (P4.4).</summary>
public sealed class ReportCarrierBookingNotDeliveredHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ReportCarrierBookingNotDeliveredHandler _handler;

    public ReportCarrierBookingNotDeliveredHandlerTests()
    {
        _handler = new ReportCarrierBookingNotDeliveredHandler(_carrierBookings, _shipments, _time);
    }

    [Fact]
    public async Task Handle_ARequestedBooking_MovesItToNotDelivered_AndReturnsTheShipmentToPlanned()
    {
        var (booking, shipment) = ReleasedBooking();

        var result = await _handler.Handle(
            new ReportCarrierBookingNotDeliveredCommand("CB-0000001", "corr-release", 503, "Service unavailable."), CancellationToken.None);

        result.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", CarrierBookingStatus.NotDelivered));
        booking.NotDeliveredStatusCode.Should().Be(503);
        booking.NotDeliveredMessage.Should().Be("Service unavailable.");
        shipment.Status.Should().Be(ShipmentStatus.Planned);
        booking.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CarrierBookingNotDeliveredEvent>();
    }

    [Fact]
    public async Task Handle_ARequestedBooking_SavesTheChangeInOneSave()
    {
        ReleasedBooking();

        await _handler.Handle(new ReportCarrierBookingNotDeliveredCommand("CB-0000001", "corr-release", 503, "Service unavailable."), CancellationToken.None);

        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var result = await _handler.Handle(
            new ReportCarrierBookingNotDeliveredCommand("CB-9999999", "corr-release", 503, "Service unavailable."), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotFound));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ABookingThatIsNoLongerRequested_IsIgnored_AndChangesNothing()
    {
        var (booking, shipment) = ReleasedBooking();
        booking.Withdraw(new IdempotencyKey("key-withdraw"), new CorrelationId("corr-withdraw"), Samples.Now.AddHours(2));
        booking.ClearDomainEvents();

        var result = await _handler.Handle(
            new ReportCarrierBookingNotDeliveredCommand("CB-0000001", "corr-release", 503, "Service unavailable."), CancellationToken.None);

        result.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", CarrierBookingStatus.Withdrawn, Ignored: true));
        booking.DomainEvents.Should().BeEmpty();
        shipment.Status.Should().Be(ShipmentStatus.Released, "the ignored report doesn't touch the Shipment");
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
