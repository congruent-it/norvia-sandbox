using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.TrackingEvents.Record;
using Norvia.Tms.Application.UnitTests.Exceptions;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.TrackingEvents;

/// <summary>A Driver's Stop report (P12) as a Tracking event from Own fleet: found by Trip, decided by the Driver report rules.</summary>
public sealed class RecordOwnFleetTrackingTests
{
    private static readonly DriverId Joost = new("DRV-0001");
    private static readonly TrackingLocation Place = new("Utrecht", "3511 AW", "NL");

    private readonly List<TrackingEvent> _storedEvents = [];
    private readonly List<Shipment> _storedShipments = [];
    private readonly List<Trip> _storedTrips = [];
    private readonly IRepository<TrackingEvent> _trackingEvents;
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IExceptionNumbers _exceptionNumbers = Substitute.For<IExceptionNumbers>();
    private readonly IReadRepository<TransportOrder> _transportOrders = Substitute.For<IReadRepository<TransportOrder>>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(2));
    private readonly RecordTrackingHandler _handler;
    private readonly Shipment _shipment;
    private readonly Trip _trip;

    public RecordOwnFleetTrackingTests()
    {
        _exceptionNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ExceptionNumber.FromSequence(9));
        _trackingEvents = InMemoryRepository.Over(_storedEvents);
        var billing = new BillingCheck(
            _transportOrders, Substitute.For<IReadRepository<Shipment>>(), Substitute.For<IReadRepository<TrackingEvent>>(), Substitute.For<IReadRepository<ProofOfDelivery>>(), Substitute.For<IReadRepository<ExceptionCase>>());
        _handler = new RecordTrackingHandler(
            _trackingEvents, Substitute.For<IRepository<CarrierBooking>>(), InMemoryRepository.Over(_storedShipments), _transportOrders, InMemoryRepository.Over(_storedTrips).AsRead(),
            new ExceptionCauses(_exceptions, _exceptionNumbers), billing, new TrackingEventRules(TimeSpan.FromMinutes(5)), _time);

        var order = Samples.AcceptedOrder(sequence: 7);
        _shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 12);
        _shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        _shipment.Dispatch();
        _shipment.ClearDomainEvents();
        _storedShipments.Add(_shipment);
        _transportOrders.SingleOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        _trip = Trip.CreateDraft(TripNumber.FromSequence(7), new DateOnly(2026, 9, 26), new VehicleId("VEH-0002"), Joost, new IdempotencyKey("k"));
        _trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        _trip.AddShipment(_shipment.Id);
        _trip.Dispatch(new CorrelationId("d"), Samples.Now, new Vehicle(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true), new Driver(Joost, "Joost", new HashSet<LicenceCategory>(), true), []);
        _trip.Start(new CorrelationId("s"), Samples.Now.AddDays(2));
        _trip.ClearDomainEvents();
        _storedTrips.Add(_trip);
    }

    [Fact]
    public async Task Handle_Collected_MovesTheShipmentToCollected_AndStoresTheEventWithItsTripAndDriver_UnderTheDriversCorrelationId()
    {
        var result = await _handler.Handle(Report(TrackingEventType.Collected, "drv-1", "corr-collect"), CancellationToken.None);

        result.Value.Should().Be(new RecordTrackingResult("SH-0000012", ShipmentStatus.Collected, "drv-1", MovedShipment: true, "corr-collect"));
        var stored = _storedEvents.Should().ContainSingle().Subject;
        stored.Source.Should().Be(TrackingSource.OwnFleet);
        stored.TripNumber.Should().Be(_trip.Number);
        stored.DriverId.Should().Be(Joost);
        stored.TripVersion.Should().Be(1);
        stored.CarrierBookingReference.Should().BeNull();
        stored.CorrelationId.Should().Be(new CorrelationId("corr-collect"));
        _shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentCollectedEvent>();
    }

    [Fact]
    public async Task Handle_DeliveredAfterCollected_MovesTheShipmentToDelivered_WithoutACarrierCost()
    {
        await _handler.Handle(Report(TrackingEventType.Collected, "drv-1"), CancellationToken.None);
        _shipment.ClearDomainEvents();

        var result = await _handler.Handle(Report(TrackingEventType.Delivered, "drv-2"), CancellationToken.None);

        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Delivered);
        _shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentDeliveredEvent>();
    }

    [Fact]
    public async Task Handle_ASecondOutcomeForTheStop_IsStopAlreadyCompleted_AndRecordsNothing()
    {
        await _handler.Handle(Report(TrackingEventType.Collected, "drv-1"), CancellationToken.None);

        var result = await _handler.Handle(Report(TrackingEventType.CollectionFailed, "drv-2"), CancellationToken.None);

        Refusal(result).Should().Be(TrackingEventRefusalCode.StopAlreadyCompleted);
        _storedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ADeliveryBeforeItsCollection_IsCollectionBeforeDelivery()
    {
        var result = await _handler.Handle(Report(TrackingEventType.Delivered, "drv-1"), CancellationToken.None);

        Refusal(result).Should().Be(TrackingEventRefusalCode.CollectionBeforeDelivery);
        _shipment.Status.Should().Be(ShipmentStatus.Dispatched);
        _storedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AShipmentTakenOffTheTrip_IsShipmentNotOnTrip_EvenWhenTheReportNamesTheOlderVersion()
    {
        var other = Samples.Planned(Samples.AcceptedOrder(sequence: 8), CarriedBy.OwnFleet, sequence: 13);
        _storedShipments.Add(other);

        var result = await _handler.Handle(Report(TrackingEventType.Collected, "drv-1", shipment: other, tripVersion: 0), CancellationToken.None);

        Refusal(result).Should().Be(TrackingEventRefusalCode.ShipmentNotOnTrip);
        other.Status.Should().Be(ShipmentStatus.Planned);
    }

    [Fact]
    public async Task Handle_AnotherDriversReport_IsTripNotFound()
    {
        var result = await _handler.Handle(Report(TrackingEventType.Collected, "drv-1", driver: new DriverId("DRV-0099")), CancellationToken.None);

        Refusal(result).Should().Be(TrackingEventRefusalCode.TripNotFound);
    }

    [Theory]
    [InlineData(TrackingEventType.CollectionFailed, ExceptionType.CollectionFailed)]
    [InlineData(TrackingEventType.Damaged, ExceptionType.Damaged)]
    public async Task Handle_AnExceptionReport_ChangesNoStatus_OpensTheMatchingException_AndRaisesShipmentExceptionReported(TrackingEventType type, ExceptionType opened)
    {
        var result = await _handler.Handle(Report(type, "drv-1", "corr-fail"), CancellationToken.None);

        result.Value.Should().Be(new RecordTrackingResult("SH-0000012", ShipmentStatus.Dispatched, "drv-1", MovedShipment: false, "corr-fail"));
        _shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentExceptionReportedEvent>();
        var exception = (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;
        exception.Type.Should().Be(opened);
        exception.CorrelationId.Should().Be(new CorrelationId("corr-fail"));
    }

    [Fact]
    public async Task Handle_DamageIsNoOutcome_SoTheStopCanStillBeCompletedAfterIt_AndDamageCanComeAfterAnOutcome()
    {
        (await _handler.Handle(Report(TrackingEventType.Damaged, "drv-1"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await _handler.Handle(Report(TrackingEventType.Collected, "drv-2"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await _handler.Handle(Report(TrackingEventType.Damaged, "drv-3"), CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ARepeatedDriverAppEventId_ReturnsTheOriginalResult_FlaggedAsARepeat_PublishingNothing_AlsoOnceTheTripIsNotUnderWay()
    {
        var first = await _handler.Handle(Report(TrackingEventType.Collected, "drv-1", "corr-first"), CancellationToken.None);
        _shipment.ClearDomainEvents();
        _trackingEvents.ClearReceivedCalls();
        _trip.Invoking(t => t.Pick(null, null)).Should().Throw<InvalidOperationException>("the Trip is under way, and stays so; the repeat does not depend on the rules");

        var repeat = await _handler.Handle(Report(TrackingEventType.Collected, "drv-1", "corr-second"), CancellationToken.None);

        repeat.Value.Should().Be(first.Value with { Repeat = true });
        _shipment.DomainEvents.Should().BeEmpty();
        await _trackingEvents.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _storedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AnEventTimeInTheFuture_IsRefused()
    {
        var result = await _handler.Handle(Report(TrackingEventType.Collected, "drv-1", eventTime: _time.GetUtcNow().AddMinutes(6)), CancellationToken.None);

        Refusal(result).Should().Be(TrackingEventRefusalCode.EventTimeInTheFuture);
    }

    private static TrackingEventRefusalCode Refusal(Result<RecordTrackingResult> result)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        return Enum.Parse<TrackingEventRefusalCode>(result.ValidationErrors.Should().ContainSingle().Subject.ErrorCode);
    }

    private RecordTrackingCommand Report(
        TrackingEventType type, string eventId, string correlationId = "corr-driver", DriverId? driver = null, Shipment? shipment = null, int tripVersion = 1, DateTimeOffset? eventTime = null) =>
        new(
            new TrackingReport(
                TrackingSource.OwnFleet, eventId, null, null, type, eventTime ?? _time.GetUtcNow().AddMinutes(-10), Place, null, "At the dock",
                new OwnFleetReport(_trip.Number, driver ?? Joost, (shipment ?? _shipment).Number, tripVersion)),
            correlationId);
}
