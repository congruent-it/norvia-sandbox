using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Application.UnitTests.Exceptions;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
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
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IReadRepository<ExceptionCase> _openExceptions = Substitute.For<IReadRepository<ExceptionCase>>();
    private readonly IExceptionNumbers _exceptionNumbers = Substitute.For<IExceptionNumbers>();
    private readonly IReadRepository<TrackingEvent> _deliveredEvents = Substitute.For<IReadRepository<TrackingEvent>>();
    private readonly IReadRepository<ProofOfDelivery> _proofsOfDelivery = Substitute.For<IReadRepository<ProofOfDelivery>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(2));
    private readonly RecordTrackingHandler _handler;

    public RecordTrackingHandlerTests()
    {
        _exceptionNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ExceptionNumber.FromSequence(9));
        _handler = new RecordTrackingHandler(_trackingEvents, _carrierBookings, _shipments, _transportOrders, new ExceptionCauses(_exceptions, _exceptionNumbers), new BillingCheck(_transportOrders, Substitute.For<IReadRepository<Shipment>>(), _deliveredEvents, _proofsOfDelivery, _openExceptions), new TrackingEventRules(TimeSpan.FromMinutes(5)), _time);
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
    public async Task Handle_DeliveryFailed_OpensADeliveryFailedException_WithTheEventsCorrelationId_InTheSameSave()
    {
        var shipment = BookedShipment();

        await _handler.Handle(Collected(type: TrackingEventType.DeliveryFailed, correlationId: "corr-ndl"), CancellationToken.None);

        var opened = (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;
        opened.Number.Value.Should().Be("EX-0000009");
        opened.Type.Should().Be(ExceptionType.DeliveryFailed);
        opened.ShipmentNumber.Should().Be(shipment.Number);
        opened.CorrelationId.Should().Be(new CorrelationId("corr-ndl"));
        opened.OpenedAt.Should().Be(Samples.Now.AddDays(2));
        opened.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>();
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _trackingEvents.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeliveryFailedWhileADeliveryFailedExceptionIsOpen_AddsACause_AndOpensNothingNew()
    {
        var shipment = BookedShipment();
        await _handler.Handle(Collected(type: TrackingEventType.DeliveryFailed, sourceEventId: "evt-1"), CancellationToken.None);
        var open = (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;
        open.ClearDomainEvents();
        _exceptions.ClearReceivedCalls();
        _exceptionNumbers.ClearReceivedCalls();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.DeliveryFailed), Arg.Any<CancellationToken>())
            .Returns(open);

        await _handler.Handle(Collected(type: TrackingEventType.DeliveryFailed, sourceEventId: "evt-2", correlationId: "corr-2"), CancellationToken.None);

        open.Causes.Select(cause => cause.SourceEventId).Should().Equal("evt-1", "evt-2");
        open.DomainEvents.Should().BeEmpty();
        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
        await _exceptionNumbers.DidNotReceive().NextAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TrackingEventType.Collected)]
    [InlineData(TrackingEventType.Departed)]
    [InlineData(TrackingEventType.Arrived)]
    [InlineData(TrackingEventType.OutForDelivery)]
    public async Task Handle_AProgressEvent_OpensNoException(TrackingEventType type)
    {
        BookedShipment();

        await _handler.Handle(Collected(type: type), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed, ExceptionType.CollectionFailed)]
    [InlineData(TrackingEventType.Damaged, ExceptionType.Damaged)]
    public async Task Handle_CollectionFailedOrDamaged_OpensAnExceptionOfThatType_WithOneExceptionOpened_InTheSameSave(TrackingEventType type, ExceptionType expected)
    {
        BookedShipment();

        await _handler.Handle(Collected(type: type, correlationId: "corr-x"), CancellationToken.None);

        var opened = OpenedException();
        opened.Type.Should().Be(expected);
        opened.CorrelationId.Should().Be(new CorrelationId("corr-x"));
        opened.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>();
        await _exceptions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _trackingEvents.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADifferentTypeWhileAnotherIsOpen_OpensASecondException()
    {
        var shipment = BookedShipment();
        await _handler.Handle(Collected(type: TrackingEventType.DeliveryFailed, sourceEventId: "evt-1"), CancellationToken.None);
        var failed = OpenedException();
        _exceptions.ClearReceivedCalls();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.DeliveryFailed), Arg.Any<CancellationToken>())
            .Returns(failed);

        await _handler.Handle(Collected(type: TrackingEventType.Damaged, sourceEventId: "evt-2"), CancellationToken.None);

        OpenedException().Type.Should().Be(ExceptionType.Damaged);
        failed.Causes.Should().ContainSingle("the Damaged event is no cause of the Delivery failed Exception");
    }

    [Fact]
    public async Task Handle_ACauseAfterTheOpenExceptionWasResolved_OpensANewException()
    {
        var shipment = BookedShipment();
        await _handler.Handle(Collected(type: TrackingEventType.Damaged, sourceEventId: "evt-1"), CancellationToken.None);
        var first = OpenedException();
        first.Resolve(ExceptionOutcome.NoActionNeeded, "Checked", new CorrelationId("corr-resolve"), Samples.Now.AddDays(3));
        _exceptions.ClearReceivedCalls();
        _exceptionNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ExceptionNumber.FromSequence(10));
        // The spec finds open Exceptions only, so a resolved one is not found.
        _exceptions.SingleOrDefaultAsync(Arg.Any<OpenExceptionOfTypeForShipmentSpec>(), Arg.Any<CancellationToken>()).Returns((ExceptionCase?)null);

        await _handler.Handle(Collected(type: TrackingEventType.Damaged, sourceEventId: "evt-2"), CancellationToken.None);

        var second = OpenedException();
        second.Number.Value.Should().Be("EX-0000010");
        second.Status.Should().Be(ExceptionStatus.Open);
        second.ShipmentId.Should().Be(shipment.Id);
    }

    [Fact]
    public async Task Handle_AnExceptionEventAfterDelivered_OpensAnExceptionMarkedAfterDelivered_AndTheShipmentStaysDelivered()
    {
        var shipment = BookedShipment();
        await _handler.Handle(Collected(type: TrackingEventType.Delivered, sourceEventId: "evt-del"), CancellationToken.None);

        var result = await _handler.Handle(Collected(type: TrackingEventType.Damaged, sourceEventId: "evt-dmg"), CancellationToken.None);

        OpenedException().AfterDelivered.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Delivered);
    }

    [Theory]
    [InlineData(TrackingEventType.Collected, ExceptionType.CollectionFailed)]
    [InlineData(TrackingEventType.Delivered, ExceptionType.DeliveryFailed)]
    public async Task Handle_AnEventThatClearsACause_MarksTheOpenExceptionCauseCleared_WithoutPublishingOrResolvingIt(TrackingEventType type, ExceptionType cleared)
    {
        var shipment = BookedShipment();
        var open = OpenExceptions.DeliveryFailed();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == cleared), Arg.Any<CancellationToken>())
            .Returns(open);
        open.ClearDomainEvents();

        await _handler.Handle(Collected(type: type), CancellationToken.None);

        open.CauseCleared.Should().BeTrue();
        open.Status.Should().Be(ExceptionStatus.Open);
        open.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CollectedWithNoOpenCollectionFailedException_ChangesNothing()
    {
        BookedShipment();

        await _handler.Handle(Collected(), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    private static readonly DateTimeOffset WindowLatest = Samples.Now.AddDays(1).AddHours(10);

    [Fact]
    public async Task Handle_AnEtaAfterTheDeliveryWindow_OpensALateDeliveryException_OnTheEventsTrail_WithOneExceptionOpened()
    {
        BookedShipment();

        await _handler.Handle(Collected(type: TrackingEventType.Departed, eta: WindowLatest.AddMinutes(1), correlationId: "corr-late"), CancellationToken.None);

        var opened = OpenedException();
        opened.Type.Should().Be(ExceptionType.LateDelivery);
        opened.CorrelationId.Should().Be(new CorrelationId("corr-late"));
        opened.Causes.Should().ContainSingle().Which.Kind.Should().Be(ExceptionCauseKind.TrackingEvent);
        opened.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExceptionOpenedEvent>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task Handle_AnEtaInsideTheDeliveryWindow_OpensNoException(int minutesFromLatest)
    {
        BookedShipment();

        await _handler.Handle(Collected(type: TrackingEventType.Departed, eta: WindowLatest.AddMinutes(minutesFromLatest)), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnEtaInsideTheAgreedDeliveryWindow_ButAfterTheOrdersOne_OpensNoException()
    {
        var shipment = BookedShipment();
        shipment.Record(
            new TrackingReport(TrackingSource.Carrier(VanDijk), "evt-0", "VDT-1", null, TrackingEventType.Collected, Samples.Now.AddDays(1), new TrackingLocation("Rotterdam", "3011 AA", "NL"), null, null),
            new CorrelationId("corr-0"),
            Samples.Now.AddDays(1));
        shipment.AgreeDeliveryWindow(new TimeWindow(WindowLatest.AddDays(1), WindowLatest.AddDays(2)), ExceptionNumber.FromSequence(5), new IdempotencyKey("key-agree"), new CorrelationId("corr-agree"), Samples.Now.AddDays(1));

        await _handler.Handle(Collected(type: TrackingEventType.Departed, eta: WindowLatest.AddDays(1).AddHours(3), sourceEventId: "evt-2"), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnotherLateEtaWhileLateDeliveryIsOpen_AddsACause_AndPublishesNothing()
    {
        var shipment = BookedShipment();
        var open = OpenExceptions.LateDelivery();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(open);

        await _handler.Handle(Collected(type: TrackingEventType.Departed, eta: WindowLatest.AddHours(3), sourceEventId: "evt-2"), CancellationToken.None);

        open.Causes.Should().HaveCount(2);
        open.DomainEvents.Should().BeEmpty();
        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnEtaBackInsideTheWindow_MarksTheOpenLateDeliveryCauseCleared()
    {
        var shipment = BookedShipment();
        var open = OpenExceptions.LateDelivery();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(open);

        await _handler.Handle(Collected(type: TrackingEventType.Departed, eta: WindowLatest.AddHours(-1)), CancellationToken.None);

        open.CauseCleared.Should().BeTrue();
        open.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheDeliveredEventThatCompletesTheConditions_MakesTheTransportOrderBillable_OnTheEventsTrail_InTheSameSave()
    {
        var shipment = BookedShipment();
        var order = RatedOrderOf(shipment);
        _proofsOfDelivery.AnyAsync(Arg.Is<CurrentProofOfDeliverySpec>(s => s.ShipmentId == shipment.Id), Arg.Any<CancellationToken>()).Returns(true);
        var eventTime = Samples.Now.AddDays(1);

        await _handler.Handle(Collected(type: TrackingEventType.Delivered, eventTime: eventTime, correlationId: "corr-delivered"), CancellationToken.None);

        order.BillingState.Should().Be(BillingState.Billable);
        order.DeliveredAt.Should().Be(eventTime, "it is the time the Delivered event says, not when the TMS recorded it");
        order.BillingCorrelationId.Should().Be(new CorrelationId("corr-delivered"));
        order.BecameBillableAt.Should().Be(Samples.Now.AddDays(2));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TransportOrderBillableEvent>();
        await _trackingEvents.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheDeliveredEventForAShipmentWithAnOpenException_LeavesTheOrderNotYetBillable()
    {
        var shipment = BookedShipment();
        var order = RatedOrderOf(shipment);
        _proofsOfDelivery.AnyAsync(Arg.Is<CurrentProofOfDeliverySpec>(s => s.ShipmentId == shipment.Id), Arg.Any<CancellationToken>()).Returns(true);
        _openExceptions.AnyAsync(Arg.Any<OpenExceptionOfShipmentSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.Handle(Collected(type: TrackingEventType.Delivered, eventTime: Samples.Now.AddDays(1)), CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TheDeliveredEventForAnOrderWithoutAProofOfDelivery_LeavesItNotYetBillable()
    {
        var shipment = BookedShipment();
        var order = RatedOrderOf(shipment);

        await _handler.Handle(Collected(type: TrackingEventType.Delivered), CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AnEventThatDoesNotDeliverTheShipment_DoesNotCheckBilling()
    {
        var shipment = BookedShipment();
        var order = RatedOrderOf(shipment);
        _proofsOfDelivery.AnyAsync(Arg.Any<CurrentProofOfDeliverySpec>(), Arg.Any<CancellationToken>()).Returns(true);

        await _handler.Handle(Collected(type: TrackingEventType.Collected), CancellationToken.None);

        order.BillingState.Should().Be(BillingState.NotYetBillable);
        await _transportOrders.DidNotReceive().SingleOrDefaultAsync(Arg.Any<TransportOrderToBillSpec>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ADeliveredEventSentAgain_DoesNotCheckBillingAgain()
    {
        var shipment = BookedShipment();
        var order = RatedOrderOf(shipment);
        await _handler.Handle(Collected(type: TrackingEventType.Delivered, sourceEventId: "evt-del"), CancellationToken.None);
        _transportOrders.ClearReceivedCalls();

        await _handler.Handle(Collected(type: TrackingEventType.Delivered, sourceEventId: "evt-del-2"), CancellationToken.None);

        await _transportOrders.DidNotReceive().SingleOrDefaultAsync(Arg.Any<TransportOrderToBillSpec>(), Arg.Any<CancellationToken>());
        order.BillingState.Should().Be(BillingState.NotYetBillable);
    }

    [Fact]
    public async Task Handle_ADeliveredEventWithALateEta_IsNotCheckedForLateDelivery_AndClearsAnOpenOne()
    {
        var shipment = BookedShipment();
        var open = OpenExceptions.LateDelivery();
        _exceptions.SingleOrDefaultAsync(Arg.Is<OpenExceptionOfTypeForShipmentSpec>(s => s.ShipmentId == shipment.Id && s.Type == ExceptionType.LateDelivery), Arg.Any<CancellationToken>())
            .Returns(open);

        await _handler.Handle(Collected(type: TrackingEventType.Delivered, eta: WindowLatest.AddHours(5)), CancellationToken.None);

        open.CauseCleared.Should().BeTrue();
        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ALateEtaAfterDelivered_OpensNoLateDelivery()
    {
        BookedShipment();
        await _handler.Handle(Collected(type: TrackingEventType.Delivered, sourceEventId: "evt-del"), CancellationToken.None);

        await _handler.Handle(Collected(type: TrackingEventType.Departed, eta: WindowLatest.AddHours(5), sourceEventId: "evt-late"), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
    }

    private ExceptionCase OpenedException() =>
        (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;

    [Fact]
    public async Task Handle_TheSameDeliveryFailedRepeated_OpensNothing()
    {
        BookedShipment();
        await _handler.Handle(Collected(type: TrackingEventType.DeliveryFailed), CancellationToken.None);
        var recorded = (TrackingEvent)_trackingEvents.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<TrackingEvent>.AddAsync)).GetArguments()[0]!;
        _trackingEvents.SingleOrDefaultAsync(Arg.Is<TrackingEventBySourceEventIdSpec>(s => s.SourceEventId == "evt-1"), Arg.Any<CancellationToken>()).Returns(recorded);
        _exceptions.ClearReceivedCalls();

        await _handler.Handle(Collected(type: TrackingEventType.DeliveryFailed), CancellationToken.None);

        await _exceptions.DidNotReceive().AddAsync(Arg.Any<ExceptionCase>(), Arg.Any<CancellationToken>());
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
        _transportOrders.SingleOrDefaultAsync(Arg.Is<TransportOrderByNumberSpec>(s => s.Number == order.Number), Arg.Any<CancellationToken>())
            .Returns(order);
        return shipment;
    }

    /// <summary>The Rated Transport Order the Shipment carries out, as the Billing check reads it.</summary>
    private TransportOrder RatedOrderOf(Shipment shipment)
    {
        var order = Samples.RatedOrder(sequence: 7);
        order.Plan(shipment.Number, CarriedBy.Carrier(VanDijk), new IdempotencyKey("plan"), new CorrelationId("corr-plan"), Samples.Now.AddHours(1));
        order.ClearDomainEvents();
        _transportOrders.SingleOrDefaultAsync(Arg.Is<TransportOrderToBillSpec>(s => s.Number == order.Number), Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private static CarrierBooking ConfirmedBooking(Shipment shipment)
    {
        var booking = CarrierBooking.Request(BookingNumber, shipment.Id, shipment.Number, VanDijk, new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        booking.Confirm("VDT-1", plannedCollectionTime: null, shipment.TransportOrderNumber, new CorrelationId("corr-answer"), Samples.Now.AddHours(2));
        return booking;
    }
}
