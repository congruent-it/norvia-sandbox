using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TrackingEvents;

public sealed class RecordTrackingHandlerTests
{
    private static readonly CarrierId VanDijk = new("CAR-0001");
    private static readonly TrackingSource VanDijkSource = TrackingSource.Carrier(VanDijk);
    private static readonly CarrierBookingNumber BookingNumber = CarrierBookingNumber.FromSequence(1);
    private static readonly TrackingLocation Location = new("Rotterdam", "3011 AA", "NL");

    private readonly IRepository<TrackingEvent> _trackingEvents = Substitute.For<IRepository<TrackingEvent>>();
    private readonly IRepository<CarrierBooking> _carrierBookings = Substitute.For<IRepository<CarrierBooking>>();
    private readonly IRepository<Shipment> _shipments = Substitute.For<IRepository<Shipment>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(2));
    private readonly RecordTrackingHandler _handler;

    public RecordTrackingHandlerTests()
    {
        _handler = new RecordTrackingHandler(_trackingEvents, _carrierBookings, _shipments, new TrackingEventRules(TimeSpan.FromMinutes(5)), _time);
    }

    [Fact]
    public async Task Handle_CollectedForABookedShipment_RecordsIt_AndMovesTheShipmentToCollected()
    {
        var shipment = BookedShipment();
        var eta = Samples.Now.AddDays(3);

        var result = await _handler.Handle(Collected(eta: eta), CancellationToken.None);

        result.Value.Should().Be(new RecordTrackingResult("SH-0000012", ShipmentStatus.Collected, "evt-1", MovedShipment: true, "corr-tracking"));
        shipment.Status.Should().Be(ShipmentStatus.Collected);
        var recorded = (TrackingEvent)_trackingEvents.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<TrackingEvent>.AddAsync)).GetArguments()[0]!;
        recorded.SourceEventId.Should().Be("evt-1");
        recorded.CarrierBookingReference.Should().Be("VDT-1");
        recorded.CorrelationId.Should().Be(new CorrelationId("corr-tracking"));
        recorded.ReceivedAt.Should().Be(Samples.Now.AddDays(2));
        recorded.Eta.Should().Be(eta);
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentCollectedEvent>();
        await _trackingEvents.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CollectedForAShipmentAlreadyCollected_RecordsIt_WithoutMovingTheShipment()
    {
        var shipment = BookedShipment();
        await _handler.Handle(Collected(), CancellationToken.None);
        shipment.ClearDomainEvents();

        var result = await _handler.Handle(Collected(sourceEventId: "evt-2"), CancellationToken.None);

        result.Value.MovedShipment.Should().BeFalse();
        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Collected);
        shipment.DomainEvents.Should().BeEmpty("a change that wasn't made isn't published");
        await _trackingEvents.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameEventIdRepeated_ReturnsTheOriginalResult_FlaggedAsARepeat_WithoutSavingAgain()
    {
        var shipment = BookedShipment();
        var first = await _handler.Handle(Collected(), CancellationToken.None);
        var recorded = (TrackingEvent)_trackingEvents.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<TrackingEvent>.AddAsync)).GetArguments()[0]!;
        _trackingEvents.SingleOrDefaultAsync(Arg.Is<TrackingEventBySourceEventIdSpec>(s => s.SourceEventId == "evt-1"), Arg.Any<CancellationToken>()).Returns(recorded);
        shipment.ClearDomainEvents();
        _trackingEvents.ClearReceivedCalls();

        var result = await _handler.Handle(Collected(correlationId: "corr-tracking-2"), CancellationToken.None);

        result.Value.Should().Be(first.Value with { Repeat = true });
        shipment.DomainEvents.Should().BeEmpty();
        await _trackingEvents.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnUnknownCarrierBookingReference_IsRefused_WithShipmentNotFound()
    {
        BookedShipment();

        var result = await _handler.Handle(Collected(reference: "VDT-9"), CancellationToken.None);

        await AssertRefused(result, TrackingEventRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_AReferenceOfAnotherCarrier_IsRefused_WithShipmentNotFound()
    {
        BookedShipment();

        var result = await _handler.Handle(Collected(source: TrackingSource.Carrier(new CarrierId("CAR-0002"))), CancellationToken.None);

        await AssertRefused(result, TrackingEventRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_OwnFleet_IsRefused_WithShipmentNotFound_UntilP12FindsItsShipmentByTrip()
    {
        BookedShipment();

        var result = await _handler.Handle(Collected(source: TrackingSource.OwnFleet), CancellationToken.None);

        await AssertRefused(result, TrackingEventRefusalCode.ShipmentNotFound);
    }

    [Fact]
    public async Task Handle_AnEventTimeInTheFuture_IsRefused_WithEventTimeInTheFuture_AndChangesNothing()
    {
        var shipment = BookedShipment();

        var result = await _handler.Handle(Collected(eventTime: _time.GetUtcNow().AddMinutes(6)), CancellationToken.None);

        await AssertRefused(result, TrackingEventRefusalCode.EventTimeInTheFuture);
        shipment.Status.Should().Be(ShipmentStatus.Booked);
    }

    [Fact]
    public async Task Handle_AnEventTimeWithinTheClockSkew_IsAccepted()
    {
        BookedShipment();

        var result = await _handler.Handle(Collected(eventTime: _time.GetUtcNow().AddMinutes(5)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AShipmentThatIsNotBookedOrLater_IsRefused_WithShipmentNotInTransport()
    {
        var planned = Samples.Planned(Samples.AcceptedOrder(sequence: 8), CarriedBy.Carrier(VanDijk), sequence: 13);
        var booking = ConfirmedBooking(planned);
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByReferenceSpec>(s => s.CarrierBookingReference == "VDT-2"), Arg.Any<CancellationToken>())
            .Returns(booking);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == planned.Number), Arg.Any<CancellationToken>()).Returns(planned);

        var result = await _handler.Handle(Collected(reference: "VDT-2"), CancellationToken.None);

        await AssertRefused(result, TrackingEventRefusalCode.ShipmentNotInTransport);
        await _trackingEvents.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private async Task AssertRefused(Result<RecordTrackingResult> result, TrackingEventRefusalCode code)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be(code.ToString());
        await _trackingEvents.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static RecordTrackingCommand Collected(
        string sourceEventId = "evt-1",
        string reference = "VDT-1",
        TrackingSource? source = null,
        DateTimeOffset? eventTime = null,
        DateTimeOffset? eta = null,
        string correlationId = "corr-tracking") =>
        new(source ?? VanDijkSource, sourceEventId, reference, TrackingEventType.Collected, eventTime ?? Samples.Now.AddDays(1), Location, eta, "Dock 4", correlationId);

    private Shipment BookedShipment()
    {
        var order = Samples.AcceptedOrder(sequence: 7);
        var shipment = Samples.Planned(order, CarriedBy.Carrier(VanDijk), sequence: 12);
        shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1), BookingNumber);
        shipment.Book();
        shipment.ClearDomainEvents();
        var booking = ConfirmedBooking(shipment);

        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByReferenceSpec>(s => s.CarrierBookingReference == "VDT-1" && s.CarrierId == VanDijk), Arg.Any<CancellationToken>())
            .Returns(booking);
        _shipments.SingleOrDefaultAsync(Arg.Is<ShipmentToChangeSpec>(s => s.Number == shipment.Number), Arg.Any<CancellationToken>())
            .Returns(shipment);
        return shipment;
    }

    private static CarrierBooking ConfirmedBooking(Shipment shipment)
    {
        var booking = CarrierBooking.Request(BookingNumber, shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        booking.Confirm("VDT-1", plannedCollectionTime: null, shipment.TransportOrderNumber, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        return booking;
    }
}
