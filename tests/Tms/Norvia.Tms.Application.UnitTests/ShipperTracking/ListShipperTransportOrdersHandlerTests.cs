using Ardalis.SharedKernel;
using Norvia.Tms.Application.ShipperTracking.ListTransportOrders;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.ShipperTracking;

/// <summary>A Shipper lists its Transport Orders with their Tracking status, ETA and last update (P8).</summary>
public sealed class ListShipperTransportOrdersHandlerTests
{
    private static readonly TrackingSource Source = TrackingSource.Carrier(new CarrierId("CAR-0001"));
    private static readonly TrackingLocation Location = new("Rotterdam", "3087 BM", "NL");

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly List<Shipment> _allShipments = [];
    private readonly List<TrackingEvent> _allEvents = [];

    public ListShipperTransportOrdersHandlerTests()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allShipments);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allEvents);
    }

    [Fact]
    public async Task Handle_ShipperWithNoTransportOrders_ListsNothing()
    {
        _orders.ListAsync(Arg.Any<ShipperTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var listed = await Handler().Handle(new ListShipperTransportOrdersQuery("SHP-0001"), CancellationToken.None);

        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnAcceptedTransportOrderWithNoShipment_IsReceived_WithTheDeliveryWindowsEndAsItsEtaAndNoLastUpdate()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _orders.ListAsync(Arg.Any<ShipperTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([order]);

        var listed = await Handler().Handle(new ListShipperTransportOrdersQuery("SHP-0001"), CancellationToken.None);

        listed.Should().ContainSingle().Which.Should().Be(new ShipperTransportOrderListItem(
            "TO-0000007",
            "BD-2026-0001",
            new ShipperPlaceView("Rotterdam", "NL"),
            new ShipperPlaceView("Utrecht", "NL"),
            TrackingStatus.Received,
            Samples.Now.AddDays(1).AddHours(10),
            null));
    }

    [Fact]
    public async Task Handle_ThatShipperAskedFor_IsTheOnlyOneTheOrdersAreReadFor()
    {
        _orders.ListAsync(Arg.Any<ShipperTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        await Handler().Handle(new ListShipperTransportOrdersQuery("SHP-0003"), CancellationToken.None);

        await _orders.Received(1).ListAsync(
            Arg.Is<ShipperTransportOrdersSpec>(spec => spec.Evaluate(new[] { Samples.AcceptedOrder() }).Count() == 0),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_APlannedShipmentThatWentBackAfterARejectedOrCancelledBooking_IsScheduled(bool released)
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Plan(order);
        if (released)
        {
            shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-release"), Samples.Now, CarrierBookingNumber.FromSequence(1));
            shipment.ReturnToPlanned();
        }

        _orders.ListAsync(Arg.Any<ShipperTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([order]);

        var listed = await Handler().Handle(new ListShipperTransportOrdersQuery("SHP-0001"), CancellationToken.None);

        listed.Should().ContainSingle().Which.TrackingStatus.Should().Be(TrackingStatus.Scheduled);
    }

    [Fact]
    public async Task Handle_ACollectedAndInTransitShipment_ShowsItsStatus_TheLastEtaAndTheLastProgressEvent()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Booked(order);
        _orders.ListAsync(Arg.Any<ShipperTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([order]);
        var eta = Samples.Now.AddDays(1).AddHours(8);
        _allEvents.Add(Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1), eta: Samples.Now.AddDays(2)));
        _allEvents.Add(Report(shipment, TrackingEventType.Arrived, "b", Samples.Now.AddHours(3), eta: eta));
        // A late exception is the Planner's to deal with, not an update for the Shipper.
        _allEvents.Add(Report(shipment, TrackingEventType.Damaged, "c", Samples.Now.AddHours(5)));

        var listed = await Handler().Handle(new ListShipperTransportOrdersQuery("SHP-0001"), CancellationToken.None);

        var item = listed.Should().ContainSingle().Subject;
        item.TrackingStatus.Should().Be(TrackingStatus.InTransit);
        item.Eta.Should().Be(eta);
        item.LastUpdate.Should().Be(Samples.Now.AddHours(3));
    }

    [Fact]
    public async Task Handle_SeveralTransportOrders_ListsTheNewestCollectionFirst()
    {
        var earlier = Samples.AcceptedOrder(sequence: 1);
        var booking = Samples.CanonicalTransportOrder();
        var laterBooking = booking with
        {
            Collection = booking.Collection! with { Window = new(Samples.Now.AddDays(5), Samples.Now.AddDays(5).AddHours(4)) },
        };
        var later = TransportOrder.Accept(
            new ShipperId("SHP-0001"),
            laterBooking.ToDetails(),
            TransportOrderNumber.FromSequence(2),
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-2"),
            Samples.Now);
        _orders.ListAsync(Arg.Any<ShipperTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns([earlier, later]);

        var listed = await Handler().Handle(new ListShipperTransportOrdersQuery("SHP-0001"), CancellationToken.None);

        listed.Select(item => item.TransportOrderNumber).Should().Equal("TO-0000002", "TO-0000001");
    }

    private ListShipperTransportOrdersHandler Handler() => new(_orders, _shipments, _events);

    private Shipment Plan(TransportOrder order)
    {
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), Samples.Now)!;
        _allShipments.Add(shipment);
        return shipment;
    }

    private Shipment Booked(TransportOrder order)
    {
        var shipment = Plan(order);
        shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-release"), Samples.Now, CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        return shipment;
    }

    private static TrackingEvent Report(Shipment shipment, TrackingEventType type, string eventId, DateTimeOffset eventTime, DateTimeOffset? eta = null) =>
        shipment.Record(
            new TrackingReport(Source, eventId, "VDT-1", null, type, eventTime, Location, eta, null),
            new CorrelationId("corr-tracking"),
            eventTime);
}
