using Ardalis.Result;
using Ardalis.SharedKernel;
using Norvia.Tms.Application.ShipperTracking.GetTransportOrder;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.ShipperTracking;

/// <summary>A Shipper gets or finds one Transport Order's tracking, the same either way (P8).</summary>
public sealed class GetShipperTransportOrderHandlerTests
{
    private static readonly TrackingSource Source = TrackingSource.Carrier(new CarrierId("CAR-0001"));

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly List<Shipment> _allShipments = [];
    private readonly List<TrackingEvent> _allEvents = [];

    public GetShipperTransportOrderHandlerTests()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allShipments);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allEvents.OrderBy(e => e.EventTime).ToList());
    }

    [Fact]
    public async Task Get_AnAcceptedTransportOrderWithNoShipment_IsReceived_WithItsWindowsAndNoProgress()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        GivenOrder(order);

        var result = await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000007"), CancellationToken.None);

        var detail = result.Value;
        detail.TransportOrderNumber.Should().Be("TO-0000007");
        detail.TrackingStatus.Should().Be(TrackingStatus.Received);
        detail.Collection.Should().Be(new Norvia.Tms.Application.ShipperTracking.ListTransportOrders.ShipperPlaceView("Rotterdam", "NL"));
        detail.CollectionWindow.Should().NotBeNull();
        detail.DeliveryWindow.Should().Be(new ShipperWindowView(order.Details.Delivery!.Window!.Earliest, order.Details.Delivery.Window.Latest));
        detail.Eta.Should().Be(Samples.Now.AddDays(1).AddHours(10));
        detail.LastUpdate.Should().BeNull();
        detail.Progress.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ProgressInEventTimeOrder_ShowsOnlyProgress_AndALateDeliveryFailedLeavesDelivered()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Booked(order);
        GivenOrder(order);
        // Arrived before Collected in arrival order, to show the event-time order.
        _allEvents.Add(Report(shipment, TrackingEventType.Departed, "b", Samples.Now.AddHours(2), "Venlo", "DE"));
        _allEvents.Add(Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1), "Rotterdam", "NL"));
        _allEvents.Add(Report(shipment, TrackingEventType.Delivered, "c", Samples.Now.AddHours(5), "Utrecht", "NL"));
        _allEvents.Add(Report(shipment, TrackingEventType.DeliveryFailed, "d", Samples.Now.AddHours(6), "Utrecht", "NL"));
        _allEvents.Add(Report(shipment, TrackingEventType.Damaged, "e", Samples.Now.AddHours(7), "Utrecht", "NL"));

        var detail = (await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000007"), CancellationToken.None)).Value;

        detail.Progress.Select(entry => entry.Type).Should().Equal("Collected", "Departed", "Delivered");
        detail.Progress[1].Place.Should().Be(new Norvia.Tms.Application.ShipperTracking.ListTransportOrders.ShipperPlaceView("Venlo", "DE"));
        detail.Progress[1].EventTime.Should().Be(Samples.Now.AddHours(2));
        detail.LastUpdate.Should().Be(Samples.Now.AddHours(5));
    }

    [Fact]
    public async Task Get_AnAgreedDeliveryWindow_ReplacesTheOrdersWindowAndTheFallbackEta()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Booked(order);
        GivenOrder(order);
        shipment.Record(
            new TrackingReport(Source, "a", "VDT-1", null, TrackingEventType.Collected, Samples.Now.AddHours(1), new("Rotterdam", "3087 BM", "NL"), null, null),
            new CorrelationId("corr-tracking"),
            Samples.Now.AddHours(1));
        var agreed = new TimeWindow(Samples.Now.AddDays(3), Samples.Now.AddDays(3).AddHours(2));
        shipment.AgreeDeliveryWindow(agreed, ExceptionNumber.FromSequence(5), new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Samples.Now.AddHours(2));

        var detail = (await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000007"), CancellationToken.None)).Value;

        detail.DeliveryWindow.Should().Be(new ShipperWindowView(agreed.Earliest, agreed.Latest));
        detail.Eta.Should().Be(agreed.Latest);
    }

    [Fact]
    public async Task Get_WithNoProofOfDelivery_ShowsNone()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        Booked(order);
        GivenOrder(order);

        var detail = (await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000007"), CancellationToken.None)).Value;

        detail.ProofOfDelivery.Should().BeNull();
    }

    [Fact]
    public async Task Get_ShowsTheCurrentProofOfDeliveryWithItsFiles()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        GivenCurrentProof(Booked(order));
        GivenOrder(order);

        var detail = (await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000007"), CancellationToken.None)).Value;

        detail.ProofOfDelivery.Should().BeEquivalentTo(ExpectedProof);
    }

    [Fact]
    public async Task Find_ShowsTheCurrentProofOfDeliveryWithItsFiles()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        GivenCurrentProof(Booked(order));
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByReferenceSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var detail = (await FindHandler().Handle(new FindShipperTransportOrderQuery("SHP-0001", "BD-2026-0001"), CancellationToken.None)).Value;

        detail.ProofOfDelivery.Should().BeEquivalentTo(ExpectedProof);
    }

    [Theory]
    [InlineData("TO-0000099")]
    [InlineData("not-a-number")]
    public async Task Get_AnUnknownTransportOrderOrAnotherShippers_IsNotFound(string number)
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0003", number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Single().ErrorCode.Should().Be("TransportOrderNotFound");
    }

    [Fact]
    public async Task Find_ByShipperReference_ReturnsTheSameAsGet()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        Booked(order);
        GivenOrder(order);
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByReferenceSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var got = await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000007"), CancellationToken.None);
        var found = await FindHandler().Handle(new FindShipperTransportOrderQuery("SHP-0001", "BD-2026-0001"), CancellationToken.None);

        found.Value.Should().BeEquivalentTo(got.Value);
    }

    [Fact]
    public async Task Find_AnUnknownReference_IsNotFound_TheSameAsAnUnknownNumber()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByReferenceSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await FindHandler().Handle(new FindShipperTransportOrderQuery("SHP-0001", "nope"), CancellationToken.None);

        result.ValidationErrors.Single().Should().BeEquivalentTo(
            (await GetHandler().Handle(new GetShipperTransportOrderQuery("SHP-0001", "TO-0000099"), CancellationToken.None)).ValidationErrors.Single());
    }

    private static readonly Guid CmrId = Guid.Parse("0197a1b2-0000-7000-8000-000000000001");

    private static readonly ShipperProofOfDelivery ExpectedProof = new(
        "J. Becker", Samples.Now, DeliveryCondition.Reservations, [new ShipperProofOfDeliveryFile(CmrId, "cmr.pdf", "application/pdf", 4)]);

    private void GivenCurrentProof(Shipment shipment)
    {
        var proof = ProofOfDelivery.Record(
            shipment,
            new ProofOfDeliveryReport(
                ProofOfDeliverySource.Carrier(new CarrierId("CAR-0001")), "doc-1", "VDT-1", "J. Becker", Samples.Now, new TrackingLocation("Utrecht", "3511 AA", "NL"),
                DeliveryCondition.Reservations, "One pallet damaged", [new ProofOfDeliveryFile(CmrId, "cmr.pdf", "application/pdf", 4)]),
            new CorrelationId("corr-pod"),
            Samples.Now);
        _proofs.FirstOrDefaultAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(proof);
    }

    private void GivenOrder(TransportOrder order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<ShipperTransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    private GetShipperTransportOrderHandler GetHandler() => new(_orders, _shipments, _events, _proofs);

    private FindShipperTransportOrderHandler FindHandler() => new(_orders, _shipments, _events, _proofs);

    private Shipment Booked(TransportOrder order)
    {
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), Samples.Now)!;
        _allShipments.Add(shipment);
        shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-release"), Samples.Now, CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        return shipment;
    }

    private static TrackingEvent Report(Shipment shipment, TrackingEventType type, string eventId, DateTimeOffset eventTime, string place, string country) =>
        shipment.Record(
            new TrackingReport(Source, eventId, "VDT-1", null, type, eventTime, new TrackingLocation(place, "1000 AA", country), null, "a remark"),
            new CorrelationId("corr-tracking"),
            eventTime);
}
