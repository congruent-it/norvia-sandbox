using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TrackingEvents;

public sealed class PublishShipmentCollectedTests
{
    private static readonly DateTimeOffset EventTime = new(2026, 9, 25, 9, 40, 0, TimeSpan.FromHours(2));
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_Collected_PublishesShipmentCollected_WithWhatTheEventSaid()
    {
        var collected = Collected(eta: EventTime.AddDays(1));

        await new PublishShipmentCollected(_outbox).Handle(collected, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentCollectedV1>(e =>
            e.ShipmentNumber == "SH-0000012"
            && e.TransportOrderNumber == "TO-0000007"
            && e.CarrierId == "CAR-0001"
            && e.CarrierBookingReference == "VDT-1"
            && e.EventTime == EventTime
            && e.EventTime.Offset == TimeSpan.FromHours(2)
            && e.Location == new TrackingLocationV1("Rotterdam", "3011 AA", "NL")
            && e.Eta == EventTime.AddDays(1)
            && e.CorrelationId == "corr-tracking"
            && e.OccurredAt == Samples.Now.AddDays(2)));
    }

    [Fact]
    public void ShipmentCollected_TracksItsKeys_AndFiltersOnWhoCarriesTheShipment()
    {
        var collectedEvent = ShipmentCollectedV1.From(Collected(eta: null));

        collectedEvent.Topic.Should().Be("shipment-events");
        collectedEvent.EventType.Should().Be("ShipmentCollected");
        collectedEvent.EventVersion.Should().Be(1);
        collectedEvent.Eta.Should().BeNull();
        collectedEvent.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ShipmentCollected",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-tracking",
            ["flow"] = "carrier-tracking",
            ["partyId"] = "CAR-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["assignedTo"] = "Carrier",
        });
    }

    private static ShipmentCollectedEvent Collected(DateTimeOffset? eta)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), Norvia.Tms.Domain.CarrierBookings.CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        shipment.ClearDomainEvents();

        shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(new CarrierId("CAR-0001")),
                "evt-1",
                "VDT-1",
                null,
                TrackingEventType.Collected,
                EventTime,
                new TrackingLocation("Rotterdam", "3011 AA", "NL"),
                eta,
                Remark: null),
            new CorrelationId("corr-tracking"),
            Samples.Now.AddDays(2));
        return (ShipmentCollectedEvent)shipment.DomainEvents.Single();
    }
}
