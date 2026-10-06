using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Shipments;

/// <summary>
/// After collection the Planner can agree a delivery window with the Carrier (P7). It is set on a Collected or In transit
/// Shipment, replaces the Transport Order's window for the Shipment, and joins the earlier ones, of which the last counts.
/// </summary>
public sealed class ShipmentDeliveryWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 0, TimeSpan.Zero);
    private static readonly ExceptionNumber Number = ExceptionNumber.FromSequence(5);
    private static readonly TransportOrderDetails OrderDetails = TransportOrderTests.SampleDetails();
    private static readonly TimeWindow Agreed = new(Now.AddDays(3), Now.AddDays(3).AddHours(4));

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    public void AgreeDeliveryWindow_OnACollectedOrInTransitShipment_KeepsIt_AndRaisesShipmentDeliveryWindowAgreed(TrackingEventType type)
    {
        var shipment = OnItsWay(type);

        var refusal = shipment.AgreeDeliveryWindow(Agreed, Number, new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Now);

        refusal.Should().BeNull();
        var agreed = shipment.AgreedDeliveryWindows.Should().ContainSingle().Subject;
        agreed.Should().Be(new AgreedDeliveryWindow(Agreed.Earliest!.Value, Agreed.Latest!.Value, Number, new CorrelationId("corr-agree"), Now, new IdempotencyKey("key-agree")));
        var raised = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentDeliveryWindowAgreedEvent>().Subject;
        raised.Shipment.Should().BeSameAs(shipment);
        raised.Agreed.Should().Be(agreed);
    }

    [Fact]
    public void AgreeDeliveryWindow_OnABookedShipment_IsRefused_WithShipmentNotInTransport_ChangingNothing()
    {
        var shipment = Booked();

        var refusal = shipment.AgreeDeliveryWindow(Agreed, Number, new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Now);

        refusal.Should().NotBeNull().And.Match<ExceptionRefusal>(r => r.Code == ExceptionRefusalCode.ShipmentNotInTransport);
        shipment.AgreedDeliveryWindows.Should().BeEmpty();
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AgreeDeliveryWindow_OnADeliveredShipment_IsRefused_WithShipmentNotInTransport()
    {
        var shipment = OnItsWay(TrackingEventType.Delivered);

        shipment.AgreeDeliveryWindow(Agreed, Number, new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Now)!.Code.Should().Be(ExceptionRefusalCode.ShipmentNotInTransport);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, null)]
    [InlineData(2, 1)]
    [InlineData(-2, -1)]
    [InlineData(-1, 0)]
    public void AgreeDeliveryWindow_ThatLacksATimeIsReversedOrEndsInThePast_IsRefused_WithDeliveryWindowInvalid(int? earliestHours, int? latestHours)
    {
        var shipment = OnItsWay(TrackingEventType.Collected);
        var window = new TimeWindow(earliestHours is { } e ? Now.AddHours(e) : null, latestHours is { } l ? Now.AddHours(l) : null);

        var refusal = shipment.AgreeDeliveryWindow(window, Number, new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Now);

        refusal!.Code.Should().Be(ExceptionRefusalCode.DeliveryWindowInvalid);
        shipment.AgreedDeliveryWindows.Should().BeEmpty();
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AgreeDeliveryWindow_Again_KeepsTheEarlierOnes_AndTheLastOneCounts_WhileTheOrdersWindowIsUnchanged()
    {
        var shipment = OnItsWay(TrackingEventType.Collected);
        var later = new TimeWindow(Now.AddDays(5), Now.AddDays(5).AddHours(2));
        shipment.AgreeDeliveryWindow(Agreed, Number, new IdempotencyKey("key-1"), new CorrelationId("corr-1"), Now);

        shipment.AgreeDeliveryWindow(later, Number, new IdempotencyKey("key-2"), new CorrelationId("corr-2"), Now.AddHours(1));

        shipment.AgreedDeliveryWindows.Select(a => a.Window).Should().Equal(Agreed, later);
        shipment.DeliveryWindowOf(OrderDetails).Should().Be(later);
    }

    [Fact]
    public void AgreedDeliveryWindowUnder_FindsTheWindowAgreedUnderTheKey()
    {
        var shipment = OnItsWay(TrackingEventType.Collected);
        shipment.AgreeDeliveryWindow(Agreed, Number, new IdempotencyKey("key-1"), new CorrelationId("corr-1"), Now);

        shipment.AgreedDeliveryWindowUnder(new IdempotencyKey("key-1"))!.CorrelationId.Should().Be(new CorrelationId("corr-1"));
        shipment.AgreedDeliveryWindowUnder(new IdempotencyKey("key-other")).Should().BeNull();
    }

    [Fact]
    public void DeliveryWindowOf_WithNoAgreedWindow_IsTheTransportOrdersWindow()
    {
        var shipment = OnItsWay(TrackingEventType.Collected);

        shipment.DeliveryWindowOf(OrderDetails).Should().Be(OrderDetails.Delivery!.Window);
        shipment.DeliveryWindowOf(null).Should().BeNull();
    }

    private static Shipment OnItsWay(TrackingEventType type)
    {
        var shipment = Booked();
        shipment.Record(
            new TrackingReport(TrackingSource.Carrier(new CarrierId("CAR-0001")), "evt-1", "VDT-1", null, type, Now, new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr-tracking"),
            Now);
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static Shipment Booked()
    {
        var order = TransportOrder.Accept(
            new Norvia.Tms.Domain.Shippers.ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            TransportOrderNumber.FromSequence(7),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-intake"),
            Now);
        var shipment = order.Plan(
            ShipmentNumber.FromSequence(3),
            CarriedBy.Carrier(new CarrierId("CAR-0001")),
            new IdempotencyKey("key-1"),
            new CorrelationId("corr-planning"),
            Now.AddHours(2))!;
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Now.AddHours(3), CarrierBookingNumber.FromSequence(42));
        shipment.Book();
        shipment.ClearDomainEvents();
        return shipment;
    }
}
