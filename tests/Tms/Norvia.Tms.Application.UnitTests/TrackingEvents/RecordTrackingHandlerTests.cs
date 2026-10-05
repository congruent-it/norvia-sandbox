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
    public async Task Handle_WithNoBookingReference_FindsTheShipmentByTheCarrierBookingNumber()
    {
        var shipment = BookedShipment();
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByNumberSpec>(s => s.Number == BookingNumber && s.CarrierId == VanDijk), Arg.Any<CancellationToken>())
            .Returns(ConfirmedBooking(shipment));

        var result = await _handler.Handle(Collected(reference: null, number: BookingNumber.Value), CancellationToken.None);

        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Collected);
        var recorded = (TrackingEvent)_trackingEvents.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<TrackingEvent>.AddAsync)).GetArguments()[0]!;
        recorded.CarrierBookingReference.Should().Be("VDT-1", "the event keeps the Carrier's own reference the booking was confirmed with");
    }

    [Fact]
    public async Task Handle_WithAReferenceThatDoesNotMatch_FallsBackToTheCarrierBookingNumber()
    {
        var shipment = BookedShipment();
        _carrierBookings.SingleOrDefaultAsync(Arg.Is<ConfirmedCarrierBookingByNumberSpec>(s => s.Number == BookingNumber), Arg.Any<CancellationToken>())
            .Returns(ConfirmedBooking(shipment));

        var result = await _handler.Handle(Collected(reference: "WRONG", number: BookingNumber.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "not-a-number")]
    [InlineData("WRONG", "CB-0000099")]
    public async Task Handle_WithNeitherAMatchingReferenceNorNumber_IsRefused_WithShipmentNotFound(string? reference, string? number)
    {
        BookedShipment();

        var result = await _handler.Handle(Collected(reference: reference, number: number), CancellationToken.None);

        result.ValidationErrors.Should().ContainSingle().Which.ErrorCode.Should().Be("ShipmentNotFound");
        await _trackingEvents.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed)]
    [InlineData(TrackingEventType.DeliveryFailed)]
    [InlineData(TrackingEventType.Damaged)]
    public async Task Handle_AnException_IsRecorded_WithoutMovingTheShipment_AndRaisesShipmentExceptionReported(TrackingEventType type)
    {
        var shipment = BookedShipment();

        var result = await _handler.Handle(Collected(type: type), CancellationToken.None);

        result.Value.Should().Be(new RecordTrackingResult("SH-0000012", ShipmentStatus.Booked, "evt-1", MovedShipment: false, "corr-tracking"));
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentExceptionReportedEvent>();
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

    [Fact]
    public async Task Handle_DeliveredForABookedShipment_StoresTheEvent_MovesTheShipment_AndRaisesTheEvent_InOneSave()
    {
        var shipment = BookedShipment();
        var command = Collected() with { Report = Collected().Report with { Type = TrackingEventType.Delivered } };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Delivered);
        shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentDeliveredEvent>();
        // The event, the Shipment's move and the event it raised (which goes to the outbox) all save together, or not at all.
        await _trackingEvents.Received(1).AddAsync(Arg.Any<TrackingEvent>(), Arg.Any<CancellationToken>());
        await _trackingEvents.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _shipments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSavingFails_TheFailureIsNotSwallowed_SoNothingIsCommitted()
    {
        BookedShipment();
        _trackingEvents.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new InvalidOperationException("the save failed"));

        var handling = async () => await _handler.Handle(Collected(), CancellationToken.None);

        await handling.Should().ThrowAsync<InvalidOperationException>();
    }

    private static RecordTrackingCommand Collected(
        string sourceEventId = "evt-1",
        string? reference = "VDT-1",
        string? number = null,
        TrackingSource? source = null,
        DateTimeOffset? eventTime = null,
        DateTimeOffset? eta = null,
        string correlationId = "corr-tracking",
        TrackingEventType type = TrackingEventType.Collected) =>
        new(
            new TrackingReport(
                source ?? VanDijkSource,
                sourceEventId,
                reference,
                number,
                type,
                eventTime ?? Samples.Now.AddDays(1),
                Location,
                eta,
                "Dock 4"),
            correlationId);

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
