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

public sealed class PublishShipmentProgressTests
{
    private static readonly DateTimeOffset EventTime = new(2026, 9, 25, 9, 40, 0, TimeSpan.FromHours(2));
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    [Fact]
    public async Task Handle_InTransit_PublishesShipmentInTransit_WithWhatTheEventSaid()
    {
        var inTransit = Moved<ShipmentInTransitEvent>(TrackingEventType.Arrived);

        await new PublishShipmentInTransit(_outbox).Handle(inTransit, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentInTransitV1>(e => Said(e) && e.EventType == "ShipmentInTransit"));
    }

    [Fact]
    public async Task Handle_Delivered_PublishesShipmentDelivered_WithWhatTheEventSaid()
    {
        var delivered = Moved<ShipmentDeliveredEvent>(TrackingEventType.Delivered);

        await new PublishShipmentDelivered(_outbox).Handle(delivered, CancellationToken.None);

        _outbox.Received(1).Add(Arg.Is<ShipmentDeliveredV1>(e => Said(e) && e.EventType == "ShipmentDelivered"));
    }

    [Fact]
    public void ShipmentDelivered_TracksItsKeys_AndFiltersOnWhoCarriesTheShipment()
    {
        var delivered = ShipmentDeliveredV1.From(Moved<ShipmentDeliveredEvent>(TrackingEventType.Delivered));

        delivered.Topic.Should().Be("shipment-events");
        delivered.EventVersion.Should().Be(1);
        delivered.ApplicationProperties.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["eventType"] = "ShipmentDelivered",
            ["eventVersion"] = 1,
            ["correlationId"] = "corr-tracking",
            ["flow"] = "carrier-tracking",
            ["partyId"] = "CAR-0001",
            ["transportOrderNumber"] = "TO-0000007",
            ["shipmentNumber"] = "SH-0000012",
            ["assignedTo"] = "Carrier",
        });
    }

    private static bool Said(ShipmentProgressV1 e) =>
        e.ShipmentNumber == "SH-0000012"
        && e.TransportOrderNumber == "TO-0000007"
        && e.CarrierId == "CAR-0001"
        && e.CarrierBookingReference == "VDT-1"
        && e.EventTime == EventTime
        && e.Location == new TrackingLocationV1("Rotterdam", "3011 AA", "NL")
        && e.Eta is null
        && e.CorrelationId == "corr-tracking"
        && e.OccurredAt == Samples.Now.AddDays(2);

    private static TEvent Moved<TEvent>(TrackingEventType type)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(new CarrierId("CAR-0001")), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        shipment.ClearDomainEvents();

        shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(new CarrierId("CAR-0001")), "evt-1", "VDT-1", type, EventTime, new TrackingLocation("Rotterdam", "3011 AA", "NL"), Eta: null, Remark: null),
            new CorrelationId("corr-tracking"),
            Samples.Now.AddDays(2));
        return (TEvent)shipment.DomainEvents.Single();
    }
}
