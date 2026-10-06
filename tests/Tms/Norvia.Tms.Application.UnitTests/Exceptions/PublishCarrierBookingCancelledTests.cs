using Norvia.Tms.Application.Exceptions.CancelCarrierBooking;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Exceptions;

public sealed class PublishCarrierBookingCancelledTests
{
    private static readonly DateTimeOffset CancelledAt = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_PublishesCarrierBookingCancelled_OnTheCancellationsTrail()
    {
        var raised = Cancelled();

        await new PublishCarrierBookingCancelled(_outbox).Handle(raised, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<CarrierBookingCancelledV1>(e =>
            e.EventType == "CarrierBookingCancelled"
            && e.CarrierBookingNumber == "CB-0000042"
            && e.CarrierBookingReference == "VDT-1"
            && e.ShipmentNumber == "SH-0000012"
            && e.CarrierId == "CAR-0001"
            && e.ExceptionNumber == "EX-0000005"
            && e.ReasonText == "No show"
            && e.CorrelationId == "corr-cancel"
            && e.OccurredAt == CancelledAt));
    }

    [Fact]
    public void CarrierBookingCancelled_IsOnTheShipmentEventsTopic_TracedOnTheExceptionHandlingFlow()
    {
        var cancelled = CarrierBookingCancelledV1.From(Cancelled());

        cancelled.Topic.Should().Be("shipment-events");
        cancelled.EventVersion.Should().Be(1);
        cancelled.ApplicationProperties.Should()
            .Contain("flow", "exception-handling").And.Contain("partyId", "CAR-0001").And.Contain("shipmentNumber", "SH-0000012")
            .And.Contain("carrierBookingNumber", "CB-0000042").And.Contain("correlationId", "corr-cancel");
    }

    private static CarrierBookingCancelledEvent Cancelled()
    {
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(42), ShipmentId.New(), ShipmentNumber.FromSequence(12), new CarrierId("CAR-0001"), new CorrelationId("corr-release"), CancelledAt.AddDays(-1));
        booking.Confirm("VDT-1", null, TransportOrderNumber.FromSequence(7), new CorrelationId("corr-answer"), CancelledAt.AddHours(-20));
        booking.Cancel("No show", ExceptionNumber.FromSequence(5), new IdempotencyKey("key-cancel-1"), new CorrelationId("corr-cancel"), CancelledAt);
        return booking.DomainEvents.OfType<CarrierBookingCancelledEvent>().Single();
    }
}
