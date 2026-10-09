using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TrackingEvents;

public sealed class PublishCarrierCostIncurredTests
{
    private static readonly DateTimeOffset DeliveryTime = new(2026, 9, 25, 9, 40, 0, TimeSpan.FromHours(2));
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_PublishesCarrierCostIncurred_WithWhatTheBookingAndTheDeliveredEventSaid()
    {
        var incurred = Incurred();

        await new PublishCarrierCostIncurred(_outbox).Handle(incurred, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<CarrierCostIncurredV1>(e =>
            e.EventType == "CarrierCostIncurred"
            && e.CarrierBookingNumber == "CB-0000001"
            && e.CarrierBookingReference == "VDT-1"
            && e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000007"
            && e.CarrierId == "CAR-0001"
            && e.CarrierCost.Total == 480m
            && e.CarrierCost.Source.Kind == "Spot"
            && e.CarrierCost.Source.Remark == "Agreed by phone"
            && e.DeliveryTime == DeliveryTime
            && e.CorrelationId == "corr-tracking"
            && e.OccurredAt == Samples.Now.AddDays(2)));
    }

    [Fact]
    public void CarrierCostIncurred_TracksItsKeys_AndFiltersOnTheCarrier()
    {
        var published = CarrierCostIncurredV1.From(Incurred());

        published.Topic.Should().Be("shipment-events");
        published.EventVersion.Should().Be(1);
        published.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "CarrierCostIncurred",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-tracking",
            ["flow"] = "carrier-tracking",
            ["partyId"] = "CAR-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["carrierBookingNumber"] = "CB-0000001",
            ["assignedTo"] = "Carrier",
        });
    }

    private static CarrierCostIncurredEvent Incurred()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var carrierId = new CarrierId("CAR-0001");
        var shipment = Samples.Planned(order, CarriedBy.Carrier(carrierId), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        var booking = CarrierBooking.Request(
            CarrierBookingNumber.FromSequence(1), shipment.Id, shipment.Number, carrierId, new CorrelationId("corr-release"), Samples.Now.AddHours(1),
            CarrierCost.Spot(new Money(480m), "Agreed by phone", Samples.Now));
        booking.Confirm("VDT-1", plannedCollectionTime: null, order.Number!, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        var delivered = shipment.Record(
            new TrackingReport(TrackingSource.Carrier(carrierId), "evt-1", "VDT-1", null, TrackingEventType.Delivered, DeliveryTime, new TrackingLocation("Rotterdam", "3011 AA", "NL"), Eta: null, Remark: null),
            new CorrelationId("corr-tracking"),
            Samples.Now.AddDays(2));
        shipment.ClearDomainEvents();

        shipment.IncurCarrierCost(booking, delivered);
        return (CarrierCostIncurredEvent)shipment.DomainEvents.Single();
    }
}
