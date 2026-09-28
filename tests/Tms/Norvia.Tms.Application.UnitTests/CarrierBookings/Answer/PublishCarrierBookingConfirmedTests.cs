using Norvia.Tms.Application.CarrierBookings.Answer;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.CarrierBookings.Answer;

public sealed class PublishCarrierBookingConfirmedTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_Confirmed_PublishesCarrierBookingConfirmed_WithTheReferenceAndTheTransportOrderNumber()
    {
        var confirmed = Confirmed();

        await new PublishCarrierBookingConfirmed(_outbox).Handle(confirmed, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<CarrierBookingConfirmedV1>(e =>
            e.CarrierBookingNumber == "CB-0000001"
            && e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000007"
            && e.CarrierId == "CAR-0001"
            && e.CarrierBookingReference == "VDT-240917-0042"
            && e.PlannedCollectionTime == null
            && e.CorrelationId == "corr-answer"
            && e.OccurredAt == Samples.Now.AddHours(2)));
    }

    [Fact]
    public void CarrierBookingConfirmed_TracksItsKeys()
    {
        var confirmedEvent = CarrierBookingConfirmedV1.From(Confirmed());

        confirmedEvent.Topic.Should().Be("shipment-events");
        confirmedEvent.EventType.Should().Be("CarrierBookingConfirmed");
        confirmedEvent.EventVersion.Should().Be(1);
        confirmedEvent.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "CarrierBookingConfirmed",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-answer",
            ["flow"] = "carrier-booking-answer",
            ["partyId"] = "CAR-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["carrierBookingNumber"] = "CB-0000001",
        });
    }

    private static CarrierBookingConfirmedEvent Confirmed()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, new CarrierId("CAR-0001"), new CorrelationId("corr-release"), Samples.Now.AddHours(1));

        booking.Confirm("VDT-240917-0042", plannedCollectionTime: null, order.Number!, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        return (CarrierBookingConfirmedEvent)booking.DomainEvents.Single();
    }
}
