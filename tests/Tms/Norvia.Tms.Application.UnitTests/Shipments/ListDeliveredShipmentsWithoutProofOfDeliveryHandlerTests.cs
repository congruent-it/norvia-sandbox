using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Application.ProofsOfDelivery.ListMissing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Shipments;

/// <summary>P6's Planner list: the Delivered Shipments without a current Proof of delivery, flagged ones first, then the oldest.</summary>
public sealed class ListDeliveredShipmentsWithoutProofOfDeliveryHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly DateTimeOffset DeliveredAt = Samples.Now.AddDays(2);

    private readonly IReadRepository<Shipment> _shipments = Substitute.For<IReadRepository<Shipment>>();
    private readonly IReadRepository<TrackingEvent> _trackingEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofs = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly ICarrierDirectory _carriers = Substitute.For<ICarrierDirectory>();
    private readonly FakeTimeProvider _time = new(DeliveredAt);
    private readonly List<Shipment> _allShipments = [];
    private readonly List<TrackingEvent> _allEvents = [];
    private readonly List<ProofOfDelivery> _allProofs = [];
    private readonly ListDeliveredShipmentsWithoutProofOfDeliveryHandler _handler;

    public ListDeliveredShipmentsWithoutProofOfDeliveryHandlerTests()
    {
        _carriers.Find(VanDijk).Returns(new Carrier(VanDijk, "Van Dijk Transport", true));
        _shipments.ListAsync(Arg.Any<DeliveredShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allShipments);
        _trackingEvents.ListAsync(Arg.Any<DeliveredTrackingEventsForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allEvents);
        _proofs.ListAsync(Arg.Any<CurrentProofsOfDeliveryForShipmentsSpec>(), Arg.Any<CancellationToken>()).Returns(_ => _allProofs);
        _handler = new(_shipments, _trackingEvents, _proofs, _carriers, new ProofOfDeliveryMissingRules(TimeSpan.FromHours(48)), _time);
    }

    [Fact]
    public async Task Handle_NoDeliveredShipments_ListsNothing()
    {
        var listed = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        listed.Should().BeEmpty();
        await _proofs.DidNotReceive().ListAsync(Arg.Any<CurrentProofsOfDeliveryForShipmentsSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADeliveredShipmentWithoutProof_ListsItWithItsCarrierAndDeliveredTime()
    {
        Delivered(sequence: 12, deliveredAt: DeliveredAt);

        var listed = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        var summary = listed.Should().ContainSingle().Subject;
        summary.ShipmentNumber.Should().Be("SH-0000012");
        summary.CarriedBy.Carrier.Should().Be(new CarrierView("CAR-0001", "Van Dijk Transport"));
        summary.DeliveredAt.Should().Be(DeliveredAt);
        summary.IsProofOfDeliveryMissing.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AShipmentWithACurrentProof_LeavesTheList()
    {
        var shipment = Delivered(sequence: 12, deliveredAt: DeliveredAt);
        _allProofs.Add(Proof(shipment));

        var listed = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_JustBeforeTheThreshold_IsNotFlagged_AndAtItIs()
    {
        Delivered(sequence: 12, deliveredAt: DeliveredAt);

        _time.SetUtcNow(DeliveredAt.AddHours(48).AddTicks(-1));
        var before = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);
        _time.SetUtcNow(DeliveredAt.AddHours(48));
        var at = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        before.Should().ContainSingle().Which.IsProofOfDeliveryMissing.Should().BeFalse();
        at.Should().ContainSingle().Which.IsProofOfDeliveryMissing.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SeveralShipments_ListsFlaggedFirst_ThenOldestDeliveredFirst()
    {
        _time.SetUtcNow(DeliveredAt.AddHours(60));
        Delivered(sequence: 1, deliveredAt: DeliveredAt.AddHours(20)); // not flagged, newer
        Delivered(sequence: 2, deliveredAt: DeliveredAt.AddHours(5)); // not flagged, older
        Delivered(sequence: 3, deliveredAt: DeliveredAt.AddHours(-1)); // flagged, oldest
        Delivered(sequence: 4, deliveredAt: DeliveredAt.AddHours(2)); // flagged, newer

        var listed = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        listed.Select(summary => summary.ShipmentNumber).Should().Equal("SH-0000003", "SH-0000004", "SH-0000002", "SH-0000001");
    }

    [Fact]
    public async Task Handle_ADeliveredEventThatDidntMoveTheShipment_DoesntSetTheDeliveredTime()
    {
        var shipment = Delivered(sequence: 12, deliveredAt: DeliveredAt);
        _allEvents.Add(DeliveredEvent(shipment, "repeat", DeliveredAt.AddHours(5)));

        var listed = await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        listed.Should().ContainSingle().Which.DeliveredAt.Should().Be(DeliveredAt);
    }

    [Fact]
    public async Task Handle_ADeliveredShipmentNoEventMovedThere_IsABug()
    {
        _allShipments.Add(Booked(sequence: 12));

        var act = async () => await _handler.Handle(new ListDeliveredShipmentsWithoutProofOfDeliveryQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private Shipment Delivered(long sequence, DateTimeOffset deliveredAt)
    {
        var shipment = Booked(sequence);
        _allShipments.Add(shipment);
        _allEvents.Add(DeliveredEvent(shipment, "delivered", deliveredAt));
        return shipment;
    }

    private static Shipment Booked(long sequence)
    {
        var order = Samples.AcceptedOrder(sequence);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence);
        shipment.Release(order, new IdempotencyKey($"key-rel{sequence}"), new CorrelationId("corr-release"), Samples.Now.AddHours(3), CarrierBookingNumber.FromSequence(sequence));
        shipment.Book();
        return shipment;
    }

    private static TrackingEvent DeliveredEvent(Shipment shipment, string sourceEventId, DateTimeOffset eventTime) =>
        shipment.Record(
            new(TrackingSource.Carrier(VanDijk), sourceEventId, "VDT-1", null, TrackingEventType.Delivered, eventTime, new TrackingLocation("Düsseldorf", "40213", "DE"), null, null),
            new CorrelationId("corr-tracking"),
            eventTime.AddMinutes(1));

    private static ProofOfDelivery Proof(Shipment shipment) =>
        ProofOfDelivery.Record(
            shipment,
            new(ProofOfDeliverySource.Carrier(VanDijk), "doc-1", "VDT-1", "J. Becker", DeliveredAt, new TrackingLocation("Düsseldorf", "40213", "DE"),
                DeliveryCondition.Clean, null, [ProofOfDeliveryFile.New("cmr.pdf", "application/pdf", 4)]),
            new CorrelationId("corr-pod"),
            DeliveredAt.AddHours(1));
}
