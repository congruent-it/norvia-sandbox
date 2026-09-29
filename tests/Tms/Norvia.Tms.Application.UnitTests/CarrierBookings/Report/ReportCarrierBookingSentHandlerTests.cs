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

/// <summary>The P4 booking Logic App reports a Requested Carrier booking as sent (P4.4).</summary>
public sealed class ReportCarrierBookingSentHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");

    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(3));
    private readonly ReportCarrierBookingSentHandler _handler;

    public ReportCarrierBookingSentHandlerTests()
    {
        _handler = new ReportCarrierBookingSentHandler(_carrierBookings, _time);
    }

    [Fact]
    public async Task Handle_ARequestedBooking_RecordsTheSentTime_AndChangesNothingElse()
    {
        var booking = ReleasedBooking();

        var result = await _handler.Handle(new ReportCarrierBookingSentCommand("CB-0000001", "corr-release"), CancellationToken.None);

        result.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", CarrierBookingStatus.Requested));
        booking.SentAt.Should().Be(Samples.Now.AddHours(3));
    }

    [Fact]
    public async Task Handle_ARequestedBooking_SavesTheChange()
    {
        ReleasedBooking();

        await _handler.Handle(new ReportCarrierBookingSentCommand("CB-0000001", "corr-release"), CancellationToken.None);

        await _carrierBookings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownBookingNumber_IsRefused_WithBookingNotFound()
    {
        var result = await _handler.Handle(new ReportCarrierBookingSentCommand("CB-9999999", "corr-release"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(nameof(CarrierBookingRefusalCode.BookingNotFound));
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ABookingThatIsNoLongerRequested_IsIgnored_AndChangesNothing()
    {
        var booking = ReleasedBooking();
        booking.Withdraw(new IdempotencyKey("key-withdraw"), new CorrelationId("corr-withdraw"), Samples.Now.AddHours(2));

        var result = await _handler.Handle(new ReportCarrierBookingSentCommand("CB-0000001", "corr-release"), CancellationToken.None);

        result.Value.Should().Be(new CarrierBookingReportResult("CB-0000001", CarrierBookingStatus.Withdrawn, Ignored: true));
        booking.SentAt.Should().BeNull();
        await _carrierBookings.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CarrierBooking ReleasedBooking()
    {
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), ShipmentId.New(), ShipmentNumber.FromSequence(12), VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<CarrierBookingByNumberSpec>(spec => spec.Number == booking.Number), Arg.Any<CancellationToken>())
            .Returns(booking);
        return booking;
    }
}
