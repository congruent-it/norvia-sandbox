using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Trips;

/// <summary>The Driver ends an In progress Trip (P12): the Shipments delivered stay on it, the others leave it, and TripCompleted lists all three groups.</summary>
public sealed class TripEndTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly Vehicle Truck = new(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true);

    private static readonly Driver Joost = new(new DriverId("DRV-0001"), "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.C }, true);

    private readonly List<TrackingEvent> _events = [];

    [Fact]
    public void End_ShipmentsDeliveredNotDeliveredAndNotCollected_LeaveTheTripAsEachDeserves_AndRaiseOneTripCompleted()
    {
        var (trip, delivered, notDelivered, notCollected) = StartedTripOfThree();
        Report(trip, delivered, TrackingEventType.Collected);
        Report(trip, delivered, TrackingEventType.Delivered);
        Report(trip, notDelivered, TrackingEventType.Collected);
        Report(trip, notDelivered, TrackingEventType.DeliveryFailed);
        Report(trip, notCollected, TrackingEventType.CollectionFailed);
        var outcomes = StopOutcomes.From(_events);
        trip.ClearDomainEvents();

        trip.EnsureCanEnd(Joost.Id, outcomes).Should().BeNull();
        trip.End(new CorrelationId("corr-end"), At.AddHours(7), outcomes, [delivered, notDelivered, notCollected]);

        trip.Status.Should().Be(TripStatus.Completed);
        trip.Version.Should().Be(1);
        trip.ShipmentIds.Should().Equal(delivered.Id);
        trip.Stops.Select(stop => stop.Position).Should().Equal(0, 1);
        delivered.Status.Should().Be(ShipmentStatus.Delivered);
        delivered.AwaitingRedelivery.Should().BeFalse();
        notDelivered.Status.Should().Be(ShipmentStatus.Collected);
        notDelivered.AwaitingRedelivery.Should().BeTrue();
        notCollected.Status.Should().Be(ShipmentStatus.Planned);
        notCollected.AwaitingRedelivery.Should().BeFalse();
        var completed = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripCompletedEvent>().Subject;
        completed.Delivered.Should().Equal(delivered.Number);
        completed.NotDelivered.Should().Equal(notDelivered.Number);
        completed.NotCollected.Should().Equal(notCollected.Number);
        completed.EndedAt.Should().Be(At.AddHours(7));
        completed.DriverId.Should().Be(Joost.Id);
        completed.CorrelationId.Should().Be(new CorrelationId("corr-end"));
    }

    [Fact]
    public void End_ANotCollectedShipmentsDeliveryStop_NeedsNoOutcome()
    {
        var (trip, shipment) = StartedTripOfOne();
        Report(trip, shipment, TrackingEventType.CollectionFailed);

        var outcomes = StopOutcomes.From(_events);

        outcomes.OpenStopsOf(trip).Should().BeEmpty();
        trip.EnsureCanEnd(Joost.Id, outcomes).Should().BeNull();
    }

    [Fact]
    public void EnsureCanEnd_AStopWithoutAnOutcome_IsStopsOpen()
    {
        var (trip, shipment) = StartedTripOfOne();
        Report(trip, shipment, TrackingEventType.Collected);

        var refusal = trip.EnsureCanEnd(Joost.Id, StopOutcomes.From(_events));

        refusal!.Code.Should().Be(TripRefusalCode.StopsOpen);
        trip.Invoking(t => t.End(new CorrelationId("c"), At, StopOutcomes.From(_events), [shipment])).Should().Throw<InvalidOperationException>();
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public void EnsureCanEnd_AnotherDriver_IsTripNotFound_AndATripThatIsNotInProgress_IsTripNotUnderWay()
    {
        var (trip, _) = StartedTripOfOne();
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), trip.Day, Truck.Id, Joost.Id, new IdempotencyKey("k2"));
        var outcomes = StopOutcomes.From([]);

        trip.EnsureCanEnd(new DriverId("DRV-0099"), outcomes)!.Code.Should().Be(TripRefusalCode.TripNotFound);
        dispatched.EnsureCanEnd(Joost.Id, outcomes)!.Code.Should().Be(TripRefusalCode.TripNotUnderWay);
    }

    [Fact]
    public void AfterEnd_TheTripIsCompleted_SoItCanNeitherEndAgainNorBeChanged()
    {
        var (trip, shipment) = StartedTripOfOne();
        Report(trip, shipment, TrackingEventType.Collected);
        Report(trip, shipment, TrackingEventType.Delivered);
        var outcomes = StopOutcomes.From(_events);
        trip.End(new CorrelationId("c"), At, outcomes, [shipment]);

        trip.EnsureCanEnd(Joost.Id, outcomes)!.Code.Should().Be(TripRefusalCode.TripNotUnderWay);
        trip.EnsureAmendable()!.Code.Should().Be(TripRefusalCode.TripCompleted);
        trip.Status.CanBeAmended().Should().BeFalse();
        trip.Invoking(t => t.End(new CorrelationId("c"), At, outcomes, [shipment])).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AwaitRedelivery_ACollectedOwnFleetShipment_IsFlagged_AndKeepsItsStatus()
    {
        var shipment = Collected();

        shipment.AwaitRedelivery().Should().BeTrue();

        shipment.AwaitingRedelivery.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Collected);
    }

    [Fact]
    public void AwaitRedelivery_AShipmentThatIsNotCollected_ChangesNothing()
    {
        var planned = Planned(1);

        planned.AwaitRedelivery().Should().BeFalse();
        planned.AwaitingRedelivery.Should().BeFalse();
    }

    private static (Trip Trip, Shipment Delivered, Shipment NotDelivered, Shipment NotCollected) StartedTripOfThree()
    {
        var shipments = new[] { Released(1), Released(2), Released(3) };
        return (Started(shipments), shipments[0], shipments[1], shipments[2]);
    }

    private static (Trip Trip, Shipment Shipment) StartedTripOfOne()
    {
        var shipment = Released(1);
        return (Started([shipment]), shipment);
    }

    private static Trip Started(Shipment[] shipments)
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), new DateOnly(2026, 10, 14), Truck.Id, Joost.Id, new IdempotencyKey("k"));
        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        foreach (var shipment in shipments)
        {
            trip.AddShipment(shipment.Id);
        }

        trip.Dispatch(new CorrelationId("dispatch"), At, Truck, Joost, [.. shipments.Select(shipment => new DispatchedShipment(shipment, TransportOrderTests.SampleDetails()))]);
        trip.Start(new CorrelationId("start"), At);
        return trip;
    }

    private void Report(Trip trip, Shipment shipment, TrackingEventType type)
    {
        var report = new TrackingReport(
            TrackingSource.OwnFleet, Guid.NewGuid().ToString(), null, null, type, At.AddHours(_events.Count + 1), new TrackingLocation("Utrecht", "3511 AW", "NL"), null, null,
            new OwnFleetReport(trip.Number, trip.DriverId!, shipment.Number, trip.Version));
        _events.Add(shipment.Record(report, new CorrelationId("driver"), At.AddHours(_events.Count + 1)));
    }

    private static Shipment Collected()
    {
        var shipment = Released(1);
        shipment.Dispatch();
        var report = new TrackingReport(
            TrackingSource.OwnFleet, "e", null, null, TrackingEventType.Collected, At, new TrackingLocation("Utrecht", "3511 AW", "NL"), null, null,
            new OwnFleetReport(TripNumber.FromSequence(1), Joost.Id, shipment.Number, 1));
        shipment.Record(report, new CorrelationId("driver"), At);
        return shipment;
    }

    private static Shipment Released(long sequence)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId("i"), At);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.OwnFleet, new IdempotencyKey($"p{sequence}"), new CorrelationId("p"), At)!;
        shipment.Release(order, new IdempotencyKey($"r{sequence}"), new CorrelationId("r"), At);
        return shipment;
    }

    private static Shipment Planned(long sequence)
    {
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId("i"), At);
        return order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.OwnFleet, new IdempotencyKey($"p{sequence}"), new CorrelationId("p"), At)!;
    }
}
