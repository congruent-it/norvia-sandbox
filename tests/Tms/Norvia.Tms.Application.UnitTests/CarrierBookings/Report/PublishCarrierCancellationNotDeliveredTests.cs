using Norvia.Tms.Application.CarrierBookings.Report;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Report;

public sealed class PublishCarrierCancellationNotDeliveredTests
{
    private static readonly DateTimeOffset ReportedAt = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_PublishesCarrierCancellationNotDelivered_OnTheReportsTrail()
    {
        var raised = NotTold();

        await new PublishCarrierCancellationNotDelivered(_outbox).Handle(raised, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<CarrierCancellationNotDeliveredV1>(e =>
            e.EventType == "CarrierCancellationNotDelivered"
            && e.CarrierBookingNumber == "CB-0000042"
            && e.ShipmentNumber == "SH-0000012"
            && e.CarrierId == "CAR-0001"
            && e.Kind == "Withdrawn"
            && e.StatusCode == 503
            && e.Message == "Service unavailable."
            && e.CorrelationId == "corr-withdraw"
            && e.OccurredAt == ReportedAt));
    }

    [Fact]
    public void CarrierCancellationNotDelivered_IsOnTheShipmentEventsTopic_TracedOnTheExceptionHandlingFlow()
    {
        var notDelivered = CarrierCancellationNotDeliveredV1.From(NotTold());

        notDelivered.Topic.Should().Be("shipment-events");
        notDelivered.EventVersion.Should().Be(1);
        notDelivered.ApplicationProperties.Should()
            .Contain("flow", "exception-handling").And.Contain("partyId", "CAR-0001").And.Contain("shipmentNumber", "SH-0000012")
            .And.Contain("carrierBookingNumber", "CB-0000042").And.Contain("correlationId", "corr-withdraw");
    }

    private static CarrierCancellationNotDeliveredEvent NotTold()
    {
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(42), ShipmentId.New(), ShipmentNumber.FromSequence(12), new CarrierId("CAR-0001"), new CorrelationId("corr-release"), ReportedAt.AddDays(-1));
        booking.Withdraw(new IdempotencyKey("key-withdraw"), new CorrelationId("corr-withdraw"), ReportedAt.AddHours(-1));
        booking.ReportCarrierNotTold(503, "Service unavailable.", new CorrelationId("corr-withdraw"), ReportedAt);
        return booking.DomainEvents.OfType<CarrierCancellationNotDeliveredEvent>().Single();
    }
}
