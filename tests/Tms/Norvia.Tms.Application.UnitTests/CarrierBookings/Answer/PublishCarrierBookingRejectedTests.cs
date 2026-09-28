using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Answer;

public sealed class PublishCarrierBookingRejectedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_Rejected_PublishesCarrierBookingRejected_WithTheReasonAndText()
    {
        var rejected = Rejected();

        await new PublishCarrierBookingRejected(_outbox).Handle(rejected, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<CarrierBookingRejectedV1>(e =>
            e.CarrierBookingNumber == "CB-0000001"
            && e.ShipmentNumber == "SH-0000012"
            && e.CarrierId == "CAR-0001"
            && e.ReasonCode == "NoCapacity"
            && e.ReasonText == "No trucks on Friday."
            && e.CorrelationId == "corr-answer"
            && e.OccurredAt == Samples.Now.AddHours(2)));
    }

    [Fact]
    public void CarrierBookingRejected_TracksItsKeys_WithTheReasonAsTheFailureCode()
    {
        var rejectedEvent = CarrierBookingRejectedV1.From(Rejected());

        rejectedEvent.Topic.Should().Be("shipment-events");
        rejectedEvent.EventType.Should().Be("CarrierBookingRejected");
        rejectedEvent.EventVersion.Should().Be(1);
        rejectedEvent.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "CarrierBookingRejected",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-answer",
            ["flow"] = "carrier-booking-answer",
            ["partyId"] = "CAR-0001",
            ["shipmentNumber"] = "SH-0000012",
            ["carrierBookingNumber"] = "CB-0000001",
            ["failureCode"] = "NoCapacity",
        });
    }

    private static CarrierBookingRejectedEvent Rejected()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, new CarrierId("CAR-0001"), new CorrelationId("corr-release"), Samples.Now.AddHours(1));

        booking.Reject(CarrierBookingRejectionReason.NoCapacity, "No trucks on Friday.", new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        return (CarrierBookingRejectedEvent)booking.DomainEvents.Single();
    }
}
