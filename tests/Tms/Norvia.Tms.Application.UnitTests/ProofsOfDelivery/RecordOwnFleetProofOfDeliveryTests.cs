using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Application.Exceptions;
using Norvia.Tms.Application.ProofsOfDelivery;
using Norvia.Tms.Application.ProofsOfDelivery.Record;
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

namespace Norvia.Tms.Application.UnitTests.ProofsOfDelivery;

/// <summary>A Driver's Proof of delivery (P12) from Own fleet: found by Trip, decided by the Driver report rules, recorded as in P6.</summary>
public sealed class RecordOwnFleetProofOfDeliveryTests
{
    private static readonly DriverId Joost = new("DRV-0001");
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly List<ProofOfDelivery> _storedProofs = [];
    private readonly List<TrackingEvent> _storedEvents = [];
    private readonly List<Shipment> _storedShipments = [];
    private readonly List<Trip> _storedTrips = [];
    private readonly IRepository<ProofOfDelivery> _proofsOfDelivery;
    private readonly IRepository<ExceptionCase> _exceptions = Substitute.For<IRepository<ExceptionCase>>();
    private readonly IProofOfDeliveryFiles _files = Substitute.For<IProofOfDeliveryFiles>();
    private readonly FakeTimeProvider _time = new(Samples.Now.AddDays(2));
    private readonly RecordProofOfDeliveryHandler _handler;
    private readonly Shipment _shipment;
    private readonly Trip _trip;

    public RecordOwnFleetProofOfDeliveryTests()
    {
        var exceptionNumbers = Substitute.For<IExceptionNumbers>();
        exceptionNumbers.NextAsync(Arg.Any<CancellationToken>()).Returns(ExceptionNumber.FromSequence(9));
        _proofsOfDelivery = InMemoryRepository.Over(_storedProofs);
        var orders = Substitute.For<IReadRepository<TransportOrder>>();
        var billing = new BillingCheck(
            orders, Substitute.For<IReadRepository<Shipment>>(), Substitute.For<IReadRepository<TrackingEvent>>(), Substitute.For<IReadRepository<ProofOfDelivery>>(), Substitute.For<IReadRepository<ExceptionCase>>());
        _handler = new RecordProofOfDeliveryHandler(
            _proofsOfDelivery, Substitute.For<IRepository<CarrierBooking>>(), InMemoryRepository.Over(_storedShipments), InMemoryRepository.Over(_storedTrips).AsRead(),
            InMemoryRepository.Over(_storedEvents).AsRead(), _files, new ExceptionCauses(_exceptions, exceptionNumbers), billing, new ProofOfDeliveryRules(TimeSpan.FromMinutes(5)), _time);

        var order = Samples.AcceptedOrder(sequence: 7);
        _shipment = Samples.Planned(order, CarriedBy.OwnFleet, sequence: 12);
        _shipment.Release(order, new IdempotencyKey("key-rel1"), new CorrelationId("corr-release"), Samples.Now.AddHours(1));
        _shipment.Dispatch();
        _shipment.ClearDomainEvents();
        _storedShipments.Add(_shipment);
        orders.SingleOrDefaultAsync(Arg.Any<TransportOrderByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        _trip = Trip.CreateDraft(TripNumber.FromSequence(7), new DateOnly(2026, 9, 26), new VehicleId("VEH-0002"), Joost, new IdempotencyKey("k"));
        _trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        _trip.AddShipment(_shipment.Id);
        _trip.Dispatch(new CorrelationId("d"), Samples.Now, new Vehicle(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true), new Driver(Joost, "Joost", new HashSet<LicenceCategory>(), true), []);
        _trip.Start(new CorrelationId("s"), Samples.Now.AddDays(2));
        _trip.ClearDomainEvents();
        _storedTrips.Add(_trip);
    }

    [Fact]
    public async Task Handle_AfterTheCollection_RecordsItAsCurrent_KeepsItsFile_PublishesTheEvent_AndLeavesTheStatusAlone()
    {
        Collect();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.ShipmentNumber.Should().Be("SH-0000012");
        result.Value.ShipmentStatus.Should().Be(ShipmentStatus.Collected);
        result.Value.CorrelationId.Should().Be("corr-pod");
        var recorded = _storedProofs.Should().ContainSingle().Subject;
        recorded.IsCurrent.Should().BeTrue();
        recorded.Source.Should().Be(ProofOfDeliverySource.OwnFleet);
        recorded.TripNumber.Should().Be(_trip.Number);
        recorded.DriverId.Should().Be(Joost);
        recorded.TripVersion.Should().Be(1);
        recorded.CarrierBookingReference.Should().BeNull();
        recorded.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProofOfDeliveryReceivedEvent>();
        await _files.Received(1).StoreAsync(_shipment.Number, Arg.Any<ProofOfDeliveryFile>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        await _proofsOfDelivery.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithReservations_OpensAReservationsException_OnTheProofsTrail()
    {
        Collect();

        await _handler.Handle(Command(Report(condition: DeliveryCondition.Reservations)), CancellationToken.None);

        var exception = (ExceptionCase)_exceptions.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRepository<ExceptionCase>.AddAsync)).GetArguments()[0]!;
        exception.Type.Should().Be(ExceptionType.Reservations);
        exception.CorrelationId.Should().Be(new CorrelationId("corr-pod"));
    }

    [Fact]
    public async Task Handle_BeforeTheCollection_IsCollectionBeforeDelivery_AndStoresNothing()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        Refusal(result).Should().Be(ProofOfDeliveryRefusalCode.CollectionBeforeDelivery);
        _storedProofs.Should().BeEmpty();
        await _files.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_OnATripThatIsNotUnderWay_IsTripNotUnderWay()
    {
        Collect();
        var ended = Trip.CreateDraft(TripNumber.FromSequence(8), new DateOnly(2026, 9, 26), new VehicleId("VEH-0002"), Joost, new IdempotencyKey("k2"));
        _storedTrips.Add(ended);

        var result = await _handler.Handle(Command(trip: ended.Number), CancellationToken.None);

        Refusal(result).Should().Be(ProofOfDeliveryRefusalCode.TripNotUnderWay);
    }

    [Fact]
    public async Task Handle_AShipmentTakenOffTheTrip_IsShipmentNotOnTrip_AndAnotherDriversTripIsTripNotFound()
    {
        Collect();
        var other = Samples.Planned(Samples.AcceptedOrder(sequence: 8), CarriedBy.OwnFleet, sequence: 13);
        _storedShipments.Add(other);

        Refusal(await _handler.Handle(Command(shipment: other, tripVersion: 0), CancellationToken.None)).Should().Be(ProofOfDeliveryRefusalCode.ShipmentNotOnTrip);
        Refusal(await _handler.Handle(Command(driver: new DriverId("DRV-0099")), CancellationToken.None)).Should().Be(ProofOfDeliveryRefusalCode.TripNotFound);
    }

    [Fact]
    public async Task Handle_ARepeatedDocumentId_ReturnsTheOriginal_FlaggedAsARepeat_RecordingAndStoringNothing()
    {
        Collect();
        var first = await _handler.Handle(Command(correlationId: "corr-first"), CancellationToken.None);
        _proofsOfDelivery.ClearReceivedCalls();
        _files.ClearReceivedCalls();

        var repeat = await _handler.Handle(Command(correlationId: "corr-second"), CancellationToken.None);

        repeat.Value.IsRepeat.Should().BeTrue();
        repeat.Value.ProofOfDeliveryId.Should().Be(first.Value.ProofOfDeliveryId);
        repeat.Value.CorrelationId.Should().Be("corr-first");
        await _files.DidNotReceiveWithAnyArgs().StoreAsync(default!, default!, default, CancellationToken.None);
        await _proofsOfDelivery.DidNotReceiveWithAnyArgs().SaveChangesAsync(CancellationToken.None);
        _storedProofs.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_FilesThatBreakP6sRules_AreRefusedAsNotAccepted()
    {
        Collect();

        var result = await _handler.Handle(Command() with { Files = [new byte[] { 1, 2, 3 }] }, CancellationToken.None);

        Refusal(result).Should().Be(ProofOfDeliveryRefusalCode.FileNotAccepted);
    }

    private void Collect() =>
        _storedEvents.Add(_shipment.Record(
            new TrackingReport(
                TrackingSource.OwnFleet, "drv-1", null, null, TrackingEventType.Collected, _time.GetUtcNow().AddMinutes(-30), new TrackingLocation("Utrecht", "3511 AW", "NL"), null, null,
                new OwnFleetReport(_trip.Number, Joost, _shipment.Number, 1)),
            new CorrelationId("corr-collect"),
            _time.GetUtcNow()));

    private static ProofOfDeliveryRefusalCode Refusal(Result<RecordProofOfDeliveryResult> result)
    {
        result.Status.Should().Be(ResultStatus.Invalid);
        return Enum.Parse<ProofOfDeliveryRefusalCode>(result.ValidationErrors.Should().ContainSingle().Subject.ErrorCode);
    }

    private ProofOfDeliveryReport Report(DriverId? driver = null, Shipment? shipment = null, int tripVersion = 1, TripNumber? trip = null, DeliveryCondition condition = DeliveryCondition.Clean) =>
        new(
            ProofOfDeliverySource.OwnFleet, "doc-1", null, "J. Becker", _time.GetUtcNow().AddMinutes(-5), new TrackingLocation("Utrecht", "3511 AW", "NL"), condition,
            condition == DeliveryCondition.Reservations ? "Pallet damaged" : null,
            [new ProofOfDeliveryFile(Guid.Parse("0197a1b2-0000-7000-8000-000000000002"), "signature.png", "image/png", Signature.Length)],
            OwnFleet: new OwnFleetReport(trip ?? _trip.Number, driver ?? Joost, (shipment ?? _shipment).Number, tripVersion));

    private RecordProofOfDeliveryCommand Command(
        ProofOfDeliveryReport? report = null, string correlationId = "corr-pod", DriverId? driver = null, Shipment? shipment = null, int tripVersion = 1, TripNumber? trip = null) =>
        new(report ?? Report(driver, shipment, tripVersion, trip), [Signature], correlationId);
}
