using Ardalis.SharedKernel;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TrackingEvents;

public sealed class TrackingSpecsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public void ShipmentsInProgressSpec_FindsTheBookedCollectedAndInTransitShipments_InTheOrderTheyWerePlanned()
    {
        var planned = Shipment(1);
        var released = Shipment(2, ShipmentStatus.Released);
        var booked = Shipment(3, ShipmentStatus.Booked, plannedAt: Now.AddMinutes(-1));
        var collected = Shipment(4, ShipmentStatus.Collected, plannedAt: Now.AddMinutes(-2));
        var inTransit = Shipment(5, ShipmentStatus.InTransit, plannedAt: Now.AddMinutes(-3));
        var delivered = Shipment(6, ShipmentStatus.Delivered);

        var found = new ShipmentsInProgressSpec().Evaluate([delivered, inTransit, planned, collected, released, booked]);

        found.Select(shipment => shipment.Number).Should().Equal(inTransit.Number, collected.Number, booked.Number);
    }

    [Fact]
    public void TrackingEventsForShipmentsSpec_FindsOnlyTheEventsOfThoseShipments()
    {
        var first = Shipment(1, ShipmentStatus.Booked);
        var second = Shipment(2, ShipmentStatus.Booked);
        var other = Shipment(3, ShipmentStatus.Booked);
        TrackingEvent[] events = [Event(first, "a", Now), Event(second, "b", Now), Event(other, "c", Now)];

        var found = new TrackingEventsForShipmentsSpec([first.Id, second.Id]).Evaluate(events);

        found.Select(trackingEvent => trackingEvent.SourceEventId).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void TrackingTimelineSpec_FindsOnlyTheShipmentsEvents_InEventTimeOrder_TheArrivalOrderBreakingATie()
    {
        var shipment = Shipment(1, ShipmentStatus.Booked);
        var other = Shipment(2, ShipmentStatus.Booked);
        var latest = Event(shipment, "latest", Now.AddHours(3), Now.AddHours(3));
        var tieArrivedLate = Event(shipment, "tie-late", Now.AddHours(1), Now.AddHours(5));
        var tieArrivedEarly = Event(shipment, "tie-early", Now.AddHours(1), Now.AddHours(4));
        var earliestButArrivedLast = Event(shipment, "earliest", Now, Now.AddHours(6));

        var found = new TrackingTimelineSpec(shipment.Id).Evaluate(
            [latest, tieArrivedLate, Event(other, "other", Now), earliestButArrivedLast, tieArrivedEarly]);

        found.Select(trackingEvent => trackingEvent.SourceEventId).Should().Equal("earliest", "tie-early", "tie-late", "latest");
    }

    private static TrackingEvent Event(Shipment shipment, string id, DateTimeOffset eventTime, DateTimeOffset? receivedAt = null) =>
        shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(new CarrierId("CAR-0001")), id, "VDT-1", null, TrackingEventType.Departed, eventTime,
                new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr"),
            receivedAt ?? Now);

    /// <summary>A Carrier Shipment, taken as far as the status says.</summary>
    private static Shipment Shipment(long sequence, ShipmentStatus status = ShipmentStatus.Planned, DateTimeOffset? plannedAt = null)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence),
            IntakeChannel.ShipperApi, new CorrelationId("corr-intake"), Now);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("k"), new CorrelationId("c"), plannedAt ?? Now)!;
        if (status >= ShipmentStatus.Released)
        {
            shipment.Release(order, new IdempotencyKey("r"), new CorrelationId("c"), Now, CarrierBookingNumber.FromSequence(sequence));
        }

        if (status >= ShipmentStatus.Booked)
        {
            shipment.Book();
        }

        foreach (var type in new[] { (ShipmentStatus.Collected, TrackingEventType.Collected), (ShipmentStatus.InTransit, TrackingEventType.Departed), (ShipmentStatus.Delivered, TrackingEventType.Delivered) })
        {
            if (status >= type.Item1)
            {
                shipment.Record(
                    new TrackingReport(
                        TrackingSource.Carrier(new CarrierId("CAR-0001")), $"setup-{type.Item2}", "VDT-1", null, type.Item2, Now.AddMinutes(-60),
                        new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
                    new CorrelationId("c"),
                    Now);
            }
        }

        return shipment;
    }
}
