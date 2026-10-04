using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>
/// A Shipment takes in the Tracking events reported for it (P5): a Collected event moves a Booked Shipment to Collected
/// and raises ShipmentCollected; on any other Shipment it is only logged, as the status only moves forward.
/// </summary>
public sealed class ShipmentTrackingTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EventTime = new(2026, 9, 25, 9, 40, 0, TimeSpan.FromHours(2));
    private static readonly TrackingLocation Location = new("Rotterdam", "3011 AA", "NL");
    private static readonly TrackingSource Source = TrackingSource.Carrier(new CarrierId("CAR-0001"));

    [Fact]
    public void Record_CollectedOnABookedShipment_MovesItToCollected_AndRaisesShipmentCollected()
    {
        var shipment = Booked();
        var eta = EventTime.AddDays(1);

        var trackingEvent = Collected(shipment, eta);

        shipment.Status.Should().Be(ShipmentStatus.Collected);
        trackingEvent.MovedShipment.Should().BeTrue();
        var raised = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentCollectedEvent>().Subject;
        raised.Shipment.Should().BeSameAs(shipment);
        raised.TrackingEvent.Should().BeSameAs(trackingEvent);
    }

    [Fact]
    public void Record_KeepsWhatTheReporterSaid()
    {
        var shipment = Booked();
        var eta = EventTime.AddDays(1);

        var trackingEvent = Collected(shipment, eta);

        trackingEvent.ShipmentId.Should().Be(shipment.Id);
        trackingEvent.ShipmentNumber.Should().Be(shipment.Number);
        trackingEvent.Source.Should().Be(Source);
        trackingEvent.SourceEventId.Should().Be("evt-1");
        trackingEvent.CarrierBookingReference.Should().Be("VDT-1");
        trackingEvent.Type.Should().Be(TrackingEventType.Collected);
        trackingEvent.EventTime.Should().Be(EventTime);
        trackingEvent.EventTime.Offset.Should().Be(TimeSpan.FromHours(2));
        trackingEvent.Location.Should().Be(Location);
        trackingEvent.Eta.Should().Be(eta);
        trackingEvent.Remark.Should().Be("Dock 4");
        trackingEvent.CorrelationId.Should().Be(new CorrelationId("corr-tracking"));
        trackingEvent.ReceivedAt.Should().Be(ReceivedAt);
    }

    [Fact]
    public void Record_CollectedOnACollectedShipment_IsOnlyLogged()
    {
        var shipment = Booked();
        Collected(shipment, eta: null);
        shipment.ClearDomainEvents();

        var later = Collected(shipment, eta: null, sourceEventId: "evt-2");

        shipment.Status.Should().Be(ShipmentStatus.Collected);
        later.MovedShipment.Should().BeFalse();
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Record_CollectedOnAShipmentThatIsNotBooked_IsOnlyLogged()
    {
        var shipment = Planned();

        var trackingEvent = Collected(shipment, eta: null);

        shipment.Status.Should().Be(ShipmentStatus.Planned);
        trackingEvent.MovedShipment.Should().BeFalse();
        shipment.DomainEvents.Should().BeEmpty();
    }

    private static TrackingEvent Collected(Shipment shipment, DateTimeOffset? eta, string sourceEventId = "evt-1") =>
        shipment.Record(
            new TrackingReport(Source, sourceEventId, "VDT-1", TrackingEventType.Collected, EventTime, Location, eta, "Dock 4"),
            new CorrelationId("corr-tracking"),
            ReceivedAt);

    private static Shipment Booked()
    {
        var (order, shipment) = PlannedWithOrder();
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), ReceivedAt.AddHours(3), CarrierBookingNumber.FromSequence(42));
        shipment.Book();
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static Shipment Planned() => PlannedWithOrder().Shipment;

    private static (TransportOrder Order, Shipment Shipment) PlannedWithOrder()
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            ReceivedAt);
        var shipment = order.Plan(
            ShipmentNumber.FromSequence(3),
            CarriedBy.Carrier(new CarrierId("CAR-0001")),
            new IdempotencyKey("key-1"),
            new CorrelationId("corr-planning"),
            ReceivedAt.AddHours(2))!;
        shipment.ClearDomainEvents();
        return (order, shipment);
    }
}
