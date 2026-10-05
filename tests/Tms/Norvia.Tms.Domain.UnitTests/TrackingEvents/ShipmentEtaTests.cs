using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TrackingEvents;

/// <summary>A Shipment's ETA is the last ETA a Carrier reported, by arrival, or the delivery window's latest time when none was (P5).</summary>
public sealed class ShipmentEtaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Window = new(Now.AddDays(1), Now.AddDays(2));

    [Fact]
    public void Of_NoEventCarriedAnEta_IsTheDeliveryWindowsLatestTime()
    {
        var events = new[] { Event("a", Now, eta: null) };

        ShipmentEta.Of(events, Window).Should().Be(Window.Latest);
    }

    [Fact]
    public void Of_NoEventsAtAll_IsTheDeliveryWindowsLatestTime()
    {
        ShipmentEta.Of([], Window).Should().Be(Window.Latest);
    }

    [Fact]
    public void Of_NeitherAnEtaNorADeliveryWindow_IsUnknown()
    {
        ShipmentEta.Of([Event("a", Now, eta: null)], deliveryWindow: null).Should().BeNull();
    }

    [Fact]
    public void Of_SeveralEtas_IsTheOneThatArrivedLast_NotTheOneWithTheLatestEventTime()
    {
        var arrivedFirst = Event("a", Now.AddHours(2), Now.AddDays(3), receivedAt: Now.AddHours(3));
        var arrivedLast = Event("b", Now.AddHours(1), Now.AddDays(1).AddHours(5), receivedAt: Now.AddHours(4));

        ShipmentEta.Of([arrivedLast, arrivedFirst], Window).Should().Be(arrivedLast.Eta);
    }

    [Fact]
    public void Of_ALaterEventWithoutAnEta_KeepsTheLastEtaReported()
    {
        var withEta = Event("a", Now, Now.AddDays(1).AddHours(2), receivedAt: Now.AddHours(1));
        var without = Event("b", Now.AddHours(1), eta: null, receivedAt: Now.AddHours(2));

        ShipmentEta.Of([withEta, without], Window).Should().Be(withEta.Eta);
    }

    private static TrackingEvent Event(string id, DateTimeOffset eventTime, DateTimeOffset? eta, DateTimeOffset? receivedAt = null)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi, new CorrelationId("corr-intake"), Now);
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("k"), new CorrelationId("c"), Now)!;
        return shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(new CarrierId("CAR-0001")), id, "VDT-1", null, TrackingEventType.Departed, eventTime,
                new TrackingLocation("Rotterdam", "3011 AA", "NL"), eta, null),
            new CorrelationId("corr"),
            receivedAt ?? Now);
    }
}
