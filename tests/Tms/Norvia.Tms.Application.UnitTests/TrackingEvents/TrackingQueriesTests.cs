using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListInProgress;
using Norvia.Tms.Application.TrackingEvents.Timeline;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TrackingEvents;

/// <summary>What is on its way, and what each Carrier actually reported (P5).</summary>
public sealed class TrackingQueriesTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly DateTimeOffset Latest = Samples.Now.AddDays(3);

    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly List<TransportOrder> _allOrders = [];

    public TrackingQueriesTests()
    {
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([]);
        _orders.ListAsync(Arg.Any<TransportOrdersByNumbersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allOrders);
    }

    [Fact]
    public async Task ListInProgress_NothingOnItsWay_ListsNothing()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task ListInProgress_ABookedShipmentWithNoEvents_ShowsNoLastEvent_AndTheDeliveryWindowsLatestTimeAsItsEta()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.Should().Be(new ShipmentInProgressSummary(
            "SH-0000012", new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport")), ShipmentStatus.Booked, null, Latest, false));
    }

    [Fact]
    public async Task ListInProgress_ShowsTheLastEventToArrive_AndTheEtaOfTheLastToArriveThatCarriedOne()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var collected = Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1), Samples.Now.AddHours(1), eta: Samples.Now.AddDays(2));
        var departed = Report(shipment, TrackingEventType.Departed, "b", Samples.Now.AddHours(3), Samples.Now.AddHours(3), eta: null);
        var lateOlder = Report(shipment, TrackingEventType.Arrived, "c", Samples.Now.AddHours(2), Samples.Now.AddHours(4), eta: Samples.Now.AddDays(1));
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([collected, departed, lateOlder]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        var summary = listed.Should().ContainSingle().Subject;
        summary.Status.Should().Be(ShipmentStatus.InTransit);
        summary.LastTrackingEvent.Should().Be(new LastTrackingEventView(TrackingEventType.Arrived, Samples.Now.AddHours(2), "Rotterdam", "3011 AA", "NL"));
        summary.Eta.Should().Be(Samples.Now.AddDays(1));
    }

    [Fact]
    public async Task ListInProgress_NotCollectedTheThresholdAfterTheCollectionWindow_IsStale_AndClearsWhenACollectedEventArrives()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var collectionLatest = Samples.Now.AddDays(1).AddHours(4);

        _time.SetUtcNow(collectionLatest.AddHours(2).AddTicks(-1));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();

        _time.SetUtcNow(collectionLatest.AddHours(2));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeTrue();

        var collected = Report(shipment, TrackingEventType.Collected, "a", _time.GetUtcNow(), _time.GetUtcNow(), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([collected]);
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task ListInProgress_SilentInTransitForTheLimit_IsStale_AndClearsWhenAnotherEventArrives()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var departed = Report(shipment, TrackingEventType.Departed, "a", Samples.Now, Samples.Now, null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([departed]);

        _time.SetUtcNow(Samples.Now.AddHours(24).AddTicks(-1));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();

        _time.SetUtcNow(Samples.Now.AddHours(24));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeTrue();

        var arrived = Report(shipment, TrackingEventType.Arrived, "b", _time.GetUtcNow(), _time.GetUtcNow(), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([departed, arrived]);
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task ListInProgress_NotDeliveredTheThresholdAfterTheDeliveryWindow_IsStale()
    {
        var shipment = Booked(sequence: 12);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([shipment]);
        var arrived = Report(shipment, TrackingEventType.Arrived, "a", Latest.AddHours(2), Latest.AddHours(2), null);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([arrived]);

        _time.SetUtcNow(Latest.AddHours(2).AddTicks(-1));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeFalse();

        _time.SetUtcNow(Latest.AddHours(2));
        (await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None)).Single().IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task ListInProgress_ListsStaleFirst_ThenByEta()
    {
        var fresh = Booked(sequence: 12, orderSequence: 7);
        var stale = Booked(sequence: 13, orderSequence: 8);
        var freshSoon = Booked(sequence: 14, orderSequence: 9);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([fresh, stale, freshSoon]);
        var freshEvent = Report(fresh, TrackingEventType.Departed, "a", Samples.Now, Samples.Now, Samples.Now.AddDays(2));
        var freshSoonEvent = Report(freshSoon, TrackingEventType.Departed, "b", Samples.Now, Samples.Now, Samples.Now.AddDays(1));
        var staleEvent = Report(stale, TrackingEventType.Departed, "c", Samples.Now.AddHours(-30), Samples.Now.AddHours(-30), Samples.Now.AddDays(5));
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns([freshEvent, freshSoonEvent, staleEvent]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Select(summary => summary.ShipmentNumber).Should().Equal("SH-0000013", "SH-0000014", "SH-0000012");
        listed.Select(summary => summary.IsStale).Should().Equal(true, false, false);
    }

    [Fact]
    public async Task ListInProgress_OnlyEventsOfTheShipmentCountForIt()
    {
        var first = Booked(sequence: 12, orderSequence: 7);
        var second = Booked(sequence: 13, orderSequence: 8);
        _shipments.ListAsync(Arg.Any<ShipmentsInProgressSpec>(), Arg.Any<CancellationToken>()).Returns([first, second]);
        _events.ListAsync(Arg.Any<TrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>())
            .Returns([Report(second, TrackingEventType.Collected, "a", Samples.Now, Samples.Now, null)]);

        var listed = await InProgressHandler().Handle(new ListShipmentsInProgressQuery(), CancellationToken.None);

        listed.Select(summary => summary.LastTrackingEvent is null).Should().Equal(true, false);
    }

    [Fact]
    public async Task Timeline_ListsTheEventsAsTheSpecOrdersThem_WithArrivalAndWhetherEachMovedTheStatus()
    {
        var shipment = Booked(sequence: 12);
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(shipment);
        var moved = Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1), Samples.Now.AddHours(2), Samples.Now.AddDays(2));
        var logged = Report(shipment, TrackingEventType.Collected, "b", Samples.Now.AddHours(2), Samples.Now.AddHours(2), null);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([moved, logged]);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(
            new TrackingTimelineEntry(
                TrackingEventType.Collected, Samples.Now.AddHours(1), "Rotterdam", "3011 AA", "NL", Samples.Now.AddDays(2), "Collected", Samples.Now.AddHours(2), true),
            new TrackingTimelineEntry(
                TrackingEventType.Collected, Samples.Now.AddHours(2), "Rotterdam", "3011 AA", "NL", null, "Collected", Samples.Now.AddHours(2), false));
    }

    [Fact]
    public async Task Timeline_AShipmentWithoutEvents_IsEmpty()
    {
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(Booked(sequence: 12));
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery("SH-0000012"), CancellationToken.None);

        result.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SH-0000099")]
    [InlineData("nonsense")]
    public async Task Timeline_AShipmentTheTmsDoesntHave_IsNotFound(string number)
    {
        _shipments.FirstOrDefaultAsync(Arg.Any<ShipmentByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((Shipment?)null);

        var result = await TimelineHandler().Handle(new GetTrackingTimelineQuery(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    private ListShipmentsInProgressHandler InProgressHandler() => new(_shipments, _events, _orders, _carriers, new ShipmentStaleRules(TimeSpan.FromHours(2), TimeSpan.FromHours(24), TimeSpan.FromHours(2)), _time);

    private GetTrackingTimelineHandler TimelineHandler() => new(_shipments, _events);

    private static TrackingEvent Report(
        Shipment shipment, TrackingEventType type, string id, DateTimeOffset eventTime, DateTimeOffset receivedAt, DateTimeOffset? eta) =>
        shipment.Record(
            new TrackingReport(
                TrackingSource.Carrier(VanDijk), id, "VDT-1", null, type, eventTime, new TrackingLocation("Rotterdam", "3011 AA", "NL"), eta, type.ToString()),
            new CorrelationId("corr"),
            receivedAt);

    /// <summary>A Booked Shipment of a Transport Order whose delivery window ends at <see cref="Latest"/>.</summary>
    private Shipment Booked(long sequence, long orderSequence = 7)
    {
        var details = TransportOrderDetailsSample();
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), details, TransportOrderNumber.FromSequence(orderSequence), IntakeChannel.ShipperApi, new CorrelationId("corr-1"), Samples.Now);
        _allOrders.Add(order);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.Carrier(VanDijk), new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), Samples.Now.AddHours(2))!;
        shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-rel"), Samples.Now.AddHours(3), CarrierBookingNumber.FromSequence(sequence));
        shipment.Book();
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static TransportOrderDetails TransportOrderDetailsSample()
    {
        var details = Samples.AcceptedOrder().Details;
        return details with { Delivery = details.Delivery! with { Window = new TimeWindow(Latest.AddHours(-4), Latest) } };
    }
}
