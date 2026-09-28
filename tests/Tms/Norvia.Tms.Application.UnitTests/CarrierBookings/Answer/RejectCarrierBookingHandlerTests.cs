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
    public async Task Handle_UnknownBookingNumber_Throws()
    {
        var reject = async () => await _handler.Handle(
            new RejectCarrierBookingCommand("CB-9999999", "CAR-0001", "corr-answer", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        await reject.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_ABookingThatIsntRequested_Throws_AndDoesNotReturnTheShipmentToPlanned()
    {
        var (booking, shipment) = ReleasedBooking();
        booking.Reject(CarrierBookingRejectionReason.LaneNotServed, null, new CorrelationId("corr-earlier-answer"), Samples.Now.AddHours(2));
        shipment.ReturnToPlanned();

        var reject = async () => await _handler.Handle(
            new RejectCarrierBookingCommand("CB-0000001", "CAR-0001", "corr-answer", CarrierBookingRejectionReason.NoCapacity, null),
            CancellationToken.None);

        await reject.Should().ThrowAsync<InvalidOperationException>();
        booking.RejectionReason.Should().Be(CarrierBookingRejectionReason.LaneNotServed, "the later, invalid answer changed nothing");
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
