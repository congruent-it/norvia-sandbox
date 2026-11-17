using System.Text.Json;
using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.ConsigneeTracking.GetView;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.ShipperTracking;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.ConsigneeTracking;

/// <summary>A Consignee opens one Transport Order with its tracking code (ADR 0052).</summary>
public sealed class GetConsigneeViewHandlerTests
{
    private static readonly TrackingSource Source = TrackingSource.Carrier(new CarrierId("CAR-0001"));
    private static readonly Guid CmrId = Guid.Parse("0197a1b2-0000-7000-8000-000000000001");

    private readonly IReadRepository<TransportOrder> _orders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _events = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddHours(12));
    private readonly List<Shipment> _allShipments = [];
    private readonly List<TrackingEvent> _allEvents = [];

    public GetConsigneeViewHandlerTests()
    {
        _shipments.ListAsync(Arg.Any<ShipmentsOfTransportOrdersSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allShipments);
        _events.ListAsync(Arg.Any<TrackingTimelineSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allEvents.OrderBy(e => e.EventTime).ToList());
    }

    [Fact]
    public async Task Get_AcceptedTransportOrderWithNoShipment_IsReceived_WithItsPlacesAndNoProgress()
    {
        var order = GivenOrder();

        var result = await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None);

        var view = result.Value;
        view.TransportOrderNumber.Should().Be(order.Number!.Value);
        view.TrackingStatus.Should().Be(TrackingStatus.Received);
        view.Collection!.City.Should().Be("Rotterdam");
        view.Delivery!.City.Should().Be("Utrecht");
        view.Eta.Should().Be(Samples.Now.AddDays(1).AddHours(10));
        view.LastUpdate.Should().BeNull();
        view.Progress.Should().BeEmpty();
        view.ProofOfDelivery.Should().BeNull();
    }

    [Fact]
    public async Task Get_ProgressOnly_InEventTimeOrder_WithoutExceptionsOrRemarks()
    {
        var order = GivenOrder();
        var shipment = Booked(order);
        _allEvents.Add(Report(shipment, TrackingEventType.Departed, "b", Samples.Now.AddHours(2)));
        _allEvents.Add(Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1)));
        _allEvents.Add(Report(shipment, TrackingEventType.Damaged, "e", Samples.Now.AddHours(7)));

        var view = (await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None)).Value;

        view.Progress.Select(entry => entry.Type).Should().Equal("Collected", "Departed");
        view.LastUpdate.Should().Be(Samples.Now.AddHours(2));
    }

    [Fact]
    public async Task Get_CurrentProofOfDelivery_ShowsItWithoutItsFiles()
    {
        var order = GivenOrder();
        var shipment = Booked(order);
        _proofs.FirstOrDefaultAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(Proof(shipment));

        var view = (await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None)).Value;

        view.ProofOfDelivery.Should().Be(new ConsigneeProofOfDelivery("J. Becker", Samples.Now, DeliveryCondition.Reservations));
    }

    [Fact]
    public async Task Get_TheView_HasNoCarrierShipmentNumberShipperReferenceExceptionRemarkStaleOrPrice()
    {
        var order = GivenOrder();
        var shipment = Booked(order);
        _allEvents.Add(Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1)));
        _proofs.FirstOrDefaultAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(Proof(shipment));

        var view = (await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None)).Value;

        var json = JsonSerializer.Serialize(view);
        foreach (var forbidden in new[] { "Carrier", "ShipmentNumber", "ShipperReference", "ShipperId", "Exception", "Remark", "Stale", "Price", "Charge", "Files", "Consignee\":", "TrackingCode", order.ShipperReference!, shipment.Number.Value })
        {
            json.Should().NotContain(forbidden);
        }
    }

    [Fact]
    public async Task Get_UnknownCode_IsTrackingCodeNotFound()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByTrackingCodeSpec>(), Arg.Any<CancellationToken>()).Returns((TransportOrder?)null);

        var result = await Handler().Handle(new GetConsigneeViewQuery("0123456789ABCDEF"), CancellationToken.None);

        AssertNotFound(result);
    }

    [Fact]
    public async Task Get_MalformedCode_IsTheSameAnswerAsAnUnknownOne_WithoutTouchingTheDatabase()
    {
        var result = await Handler().Handle(new GetConsigneeViewQuery("nope"), CancellationToken.None);

        AssertNotFound(result);
        await _orders.DidNotReceive().FirstOrDefaultAsync(Arg.Any<TransportOrderByTrackingCodeSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ExpiredAfterDelivery_IsTheSameAnswerAsAnUnknownCode()
    {
        var order = GivenOrder();
        var shipment = Booked(order);
        var delivered = Samples.Now.AddHours(5);
        _allEvents.Add(Report(shipment, TrackingEventType.Delivered, "d", delivered));
        _time.SetUtcNow(delivered + TrackingCode.Lifetime + TimeSpan.FromSeconds(1));

        var result = await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None);

        AssertNotFound(result);
    }

    [Fact]
    public async Task Get_JustBeforeItExpires_StillWorks()
    {
        var order = GivenOrder();
        var shipment = Booked(order);
        var delivered = Samples.Now.AddHours(5);
        _allEvents.Add(Report(shipment, TrackingEventType.Delivered, "d", delivered));
        _time.SetUtcNow(delivered + TrackingCode.Lifetime - TimeSpan.FromSeconds(1));

        var result = await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Get_NeverDelivered_ExpiresAfterTheLastProgressEvent()
    {
        var order = GivenOrder();
        var shipment = Booked(order);
        var last = Samples.Now.AddHours(2);
        _allEvents.Add(Report(shipment, TrackingEventType.Collected, "a", Samples.Now.AddHours(1)));
        _allEvents.Add(Report(shipment, TrackingEventType.Departed, "b", last));
        _time.SetUtcNow(last + TrackingCode.Lifetime + TimeSpan.FromSeconds(1));

        AssertNotFound(await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None));
    }

    [Fact]
    public async Task Get_NoProgressAtAll_ExpiresAfterTheTransportOrderWasReceived()
    {
        var order = GivenOrder();
        _time.SetUtcNow(order.ReceivedAt + TrackingCode.Lifetime + TimeSpan.FromSeconds(1));

        AssertNotFound(await Handler().Handle(new GetConsigneeViewQuery(order.TrackingCode!.Value), CancellationToken.None));
    }

    private static void AssertNotFound(Result<ConsigneeView> result)
    {
        result.IsInvalid().Should().BeTrue();
        var error = result.ValidationErrors.Single();
        error.ErrorCode.Should().Be(nameof(ConsigneeTrackingRefusalCode.TrackingCodeNotFound));
    }

    private TransportOrder GivenOrder()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        _orders.FirstOrDefaultAsync(Arg.Any<TransportOrderByTrackingCodeSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private GetConsigneeViewHandler Handler() => new(_orders, _shipments, _events, _proofs, _time);

    private Shipment Booked(TransportOrder order)
    {
        var shipment = order.Plan(ShipmentNumber.FromSequence(3), CarriedBy.Carrier(new CarrierId("CAR-0001")), new IdempotencyKey("key-1"), new CorrelationId("corr-planning"), Samples.Now)!;
        _allShipments.Add(shipment);
        shipment.Release(order, new IdempotencyKey("key-rel"), new CorrelationId("corr-release"), Samples.Now, CarrierBookingNumber.FromSequence(1));
        shipment.Book();
        return shipment;
    }

    private static ProofOfDelivery Proof(Shipment shipment) => ProofOfDelivery.Record(
        shipment,
        new ProofOfDeliveryReport(
            ProofOfDeliverySource.Carrier(new CarrierId("CAR-0001")), "doc-1", "VDT-1", "J. Becker", Samples.Now, new TrackingLocation("Utrecht", "3511 AA", "NL"),
            DeliveryCondition.Reservations, "One pallet damaged", [new ProofOfDeliveryFile(CmrId, "cmr.pdf", "application/pdf", 4)]),
        new CorrelationId("corr-pod"),
        Samples.Now);

    private static TrackingEvent Report(Shipment shipment, TrackingEventType type, string eventId, DateTimeOffset eventTime) =>
        shipment.Record(
            new TrackingReport(Source, eventId, "VDT-1", null, type, eventTime, new TrackingLocation("Utrecht", "1000 AA", "NL"), null, "a remark"),
            new CorrelationId("corr-tracking"),
            eventTime);
}
