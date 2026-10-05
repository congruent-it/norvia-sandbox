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
/// and raises ShipmentCollected; Departed, Arrived and Out for delivery move it to In transit and Delivered to Delivered,
/// and any step can be skipped. An event whose status isn't further along than the Shipment's is only logged.
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

    [Theory]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Arrived)]
    [InlineData(TrackingEventType.OutForDelivery)]
    public void Record_ADepartedArrivedOrOutForDeliveryEvent_MovesTheShipmentToInTransit_AndRaisesShipmentInTransit(TrackingEventType type)
    {
        var shipment = Booked();

        var trackingEvent = Report(shipment, type);

        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        trackingEvent.MovedShipment.Should().BeTrue();
        var raised = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentInTransitEvent>().Subject;
        raised.Shipment.Should().BeSameAs(shipment);
        raised.TrackingEvent.Should().BeSameAs(trackingEvent);
    }

    [Fact]
    public void Record_DeliveredOnACollectedShipment_MovesItToDelivered_AndRaisesShipmentDelivered()
    {
        var shipment = Booked();
        Collected(shipment, eta: null);
        shipment.ClearDomainEvents();

        var trackingEvent = Report(shipment, TrackingEventType.Delivered, "evt-2");

        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        trackingEvent.MovedShipment.Should().BeTrue();
        var raised = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentDeliveredEvent>().Subject;
        raised.TrackingEvent.Should().BeSameAs(trackingEvent);
    }

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed)]
    [InlineData(TrackingEventType.DeliveryFailed)]
    [InlineData(TrackingEventType.Damaged)]
    public void Record_AnException_ChangesNoStatus_AndRaisesOnlyShipmentExceptionReported(TrackingEventType type)
    {
        var shipment = Booked();

        var trackingEvent = Report(shipment, type);

        shipment.Status.Should().Be(ShipmentStatus.Booked);
        trackingEvent.MovedShipment.Should().BeFalse();
        var raised = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentExceptionReportedEvent>().Subject;
        raised.TrackingEvent.Should().BeSameAs(trackingEvent);
    }

    [Fact]
    public void Record_AnExceptionAfterDelivered_StillRaisesShipmentExceptionReported()
    {
        var shipment = Booked();
        Report(shipment, TrackingEventType.Delivered);
        shipment.ClearDomainEvents();

        Report(shipment, TrackingEventType.Damaged, "evt-2");

        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentExceptionReportedEvent>();
    }

    [Theory]
    [InlineData(TrackingEventType.Departed, ShipmentStatus.InTransit)]
    [InlineData(TrackingEventType.Delivered, ShipmentStatus.Delivered)]
    public void Record_SkippingSteps_MovesToTheStatusLandedOn_AndRaisesOnlyItsEvent(TrackingEventType type, ShipmentStatus expected)
    {
        var shipment = Booked();

        Report(shipment, type);

        shipment.Status.Should().Be(expected);
        shipment.DomainEvents.Should().ContainSingle();
    }

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Arrived)]
    public void Record_ALateEventWhoseStatusIsNotFurtherAlong_IsOnlyLogged(TrackingEventType type)
    {
        var shipment = Booked();
        Report(shipment, TrackingEventType.OutForDelivery);
        shipment.ClearDomainEvents();

        var late = Report(shipment, type, "evt-late", EventTime.AddHours(-3));

        shipment.Status.Should().Be(ShipmentStatus.InTransit);
        late.MovedShipment.Should().BeFalse();
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Record_ALateDeliveredBeforeAnEarlierEventsStatus_StillMovesTheShipmentForward()
    {
        var shipment = Booked();
        Report(shipment, TrackingEventType.Departed);
        shipment.ClearDomainEvents();

        var delivered = Report(shipment, TrackingEventType.Delivered, "evt-2", EventTime.AddHours(-3));

        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        delivered.MovedShipment.Should().BeTrue();
    }

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Delivered)]
    public void Record_AnyEventAfterDelivered_IsOnlyLogged(TrackingEventType type)
    {
        var shipment = Booked();
        Report(shipment, TrackingEventType.Delivered);
        shipment.ClearDomainEvents();

        var after = Report(shipment, type, "evt-after", EventTime.AddHours(1));

        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        after.MovedShipment.Should().BeFalse();
        shipment.DomainEvents.Should().BeEmpty();
    }

    private static TrackingEvent Collected(Shipment shipment, DateTimeOffset? eta, string sourceEventId = "evt-1") =>
        shipment.Record(
            new TrackingReport(Source, sourceEventId, "VDT-1", null, TrackingEventType.Collected, EventTime, Location, eta, "Dock 4"),
            new CorrelationId("corr-tracking"),
            ReceivedAt);

    private static TrackingEvent Report(Shipment shipment, TrackingEventType type, string sourceEventId = "evt-1", DateTimeOffset? eventTime = null) =>
        shipment.Record(
            new TrackingReport(Source, sourceEventId, "VDT-1", null, type, eventTime ?? EventTime, Location, Eta: null, Remark: null),
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
