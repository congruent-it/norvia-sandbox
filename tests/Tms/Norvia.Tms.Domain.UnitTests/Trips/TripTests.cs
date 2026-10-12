using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Domain.UnitTests.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.Trips;

public sealed class TripTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);

    [Fact]
    public void CreateDraft_WithoutVehicleAndDriver_IsAnEmptyDraftOnTheDay()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), Day, null, null, new IdempotencyKey("k"));

        trip.Number.Value.Should().Be("TR-0000007");
        trip.Day.Should().Be(Day);
        trip.Status.Should().Be(TripStatus.Draft);
        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().BeNull();
    }

    [Fact]
    public void Pick_ChangesTheVehicleAndDriverOfADraft_AndNullGivesThemBack()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, new VehicleId("VEH-0001"), null, new IdempotencyKey("k"));

        trip.Pick(new VehicleId("VEH-0002"), new DriverId("DRV-0001"));
        trip.VehicleId.Should().Be(new VehicleId("VEH-0002"));
        trip.DriverId.Should().Be(new DriverId("DRV-0001"));

        trip.Pick(null, null);
        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().BeNull();
    }

    [Fact]
    public void Plan_SetsThePlannedStartAndDrivingDurationOfADraft_AndNullGivesThemBack()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, new DriverId("DRV-0001"), new IdempotencyKey("k"));
        trip.PlannedStart.Should().BeNull();
        trip.PlannedDrivingDuration.Should().BeNull();

        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        trip.PlannedStart.Should().Be(new TimeOnly(7, 30));
        trip.PlannedDrivingDuration.Should().Be(TimeSpan.FromHours(6));
        trip.PlannedDriving.Should().Be(new PlannedDriving(new DriverId("DRV-0001"), Day, TimeSpan.FromHours(6)));

        trip.Plan(null, null);
        trip.PlannedStart.Should().BeNull();
        trip.PlannedDriving.Should().BeNull("a Trip without a duration or a Driver uses no Driving hours");
    }

    [Fact]
    public void Plan_OnATripUnderWay_IsABug()
    {
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null, new IdempotencyKey("k"));
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(dispatched, TripStatus.InProgress);

        dispatched.Invoking(trip => trip.Plan(new TimeOnly(7, 0), TimeSpan.FromHours(1))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(25 * 60)]
    public void Plan_ADurationOutsideADay_IsRejected(int minutes)
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));

        trip.Invoking(t => t.Plan(null, TimeSpan.FromMinutes(minutes))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EnsureDraft_AcceptsADraftAndRefusesADispatchedTrip_AndPickingOnOneIsABug()
    {
        var draft = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null, new IdempotencyKey("k"));
        // Dispatch isn't built yet (a later P11 slice), so put the Trip in the status it will have.
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(dispatched, TripStatus.Dispatched);

        draft.EnsureDraft().Should().BeNull();
        dispatched.EnsureDraft()!.Code.Should().Be(TripRefusalCode.TripNotDraft);
    }

    [Fact]
    public void EnsureAmendable_AcceptsADraftAndADispatchedTrip_AndRefusesATripUnderWayWithTripUnderWay()
    {
        var trip = CompleteDraft(ShipmentId.New());
        trip.EnsureAmendable().Should().BeNull();

        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);
        trip.EnsureAmendable().Should().BeNull();

        trip.Start(new CorrelationId("c"), DateTimeOffset.UtcNow);
        trip.EnsureAmendable()!.Code.Should().Be(TripRefusalCode.TripUnderWay);
        trip.Invoking(t => t.Pick(null, null)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Start_ADispatchedTrip_IsInProgress_KeepsItsVersion_AndRaisesOneTripStarted()
    {
        var trip = CompleteDraft(ShipmentId.New());
        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);
        trip.ClearDomainEvents();
        var startedAt = new DateTimeOffset(2026, 10, 13, 8, 5, 0, TimeSpan.Zero);

        trip.Start(new CorrelationId("start"), startedAt);

        trip.Status.Should().Be(TripStatus.InProgress);
        trip.Version.Should().Be(1);
        var started = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripStartedEvent>().Subject;
        started.Trip.Should().BeSameAs(trip);
        started.DriverId.Should().Be(Joost.Id);
        started.StartedAt.Should().Be(startedAt);
        started.CorrelationId.Should().Be(new CorrelationId("start"));
    }

    [Fact]
    public void Start_ADraftOrATripUnderWay_IsABug()
    {
        var draft = CompleteDraft(ShipmentId.New());
        draft.Invoking(t => t.Start(new CorrelationId("c"), DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();

        draft.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);
        draft.Start(new CorrelationId("c"), DateTimeOffset.UtcNow);
        draft.Invoking(t => t.Start(new CorrelationId("c"), DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureCanStart_ADispatchedTripOfTheDriverOnItsDay_IsNull()
    {
        var trip = CompleteDraft(ShipmentId.New());
        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);

        trip.EnsureCanStart(Joost.Id, trip.Day).Should().BeNull();
    }

    [Fact]
    public void EnsureCanStart_AnotherDriver_IsTripNotFound_EvenWhenTheTripIsADraftOrOnAnotherDay()
    {
        var trip = CompleteDraft(ShipmentId.New());

        trip.EnsureCanStart(new DriverId("DRV-0099"), trip.Day.AddDays(1))!.Code.Should().Be(TripRefusalCode.TripNotFound);
    }

    [Fact]
    public void EnsureCanStart_ADraftOrATripUnderWay_IsTripNotDispatched()
    {
        var trip = CompleteDraft(ShipmentId.New());
        trip.EnsureCanStart(Joost.Id, trip.Day)!.Code.Should().Be(TripRefusalCode.TripNotDispatched);

        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);
        trip.Start(new CorrelationId("c"), DateTimeOffset.UtcNow);
        trip.EnsureCanStart(Joost.Id, trip.Day)!.Code.Should().Be(TripRefusalCode.TripNotDispatched);
    }

    [Fact]
    public void EnsureCanStart_AnotherDay_IsNotTripDay()
    {
        var trip = CompleteDraft(ShipmentId.New());
        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);

        trip.EnsureCanStart(Joost.Id, trip.Day.AddDays(-1))!.Code.Should().Be(TripRefusalCode.NotTripDay);
        trip.EnsureCanStart(Joost.Id, trip.Day.AddDays(1))!.Code.Should().Be(TripRefusalCode.NotTripDay);
    }

    [Fact]
    public void PickPlanAndAddShipment_AnswerWhetherTheyChangedTheTrip_AlsoOnADispatchedTrip()
    {
        var first = ReleasedOwnFleetShipment(1);
        var trip = CompleteDraft(first.Id);
        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, [new DispatchedShipment(first, TransportOrderTests.SampleDetails())]);

        trip.Pick(Truck.Id, Joost.Id).Should().BeFalse();
        trip.Pick(Truck.Id, new DriverId("DRV-0002")).Should().BeTrue();
        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6)).Should().BeFalse();
        trip.Plan(new TimeOnly(9, 30), TimeSpan.FromHours(4)).Should().BeTrue();
        trip.AddShipment(first.Id).Should().BeFalse();
        trip.AddShipment(ShipmentId.New()).Should().BeTrue();
        trip.Stops.Should().HaveCount(4);
    }

    [Fact]
    public void Amend_RaisesTheVersion_StartsItsDelivery_MakesTheAddedShipmentDispatched_AndRaisesOneTripDispatchedWithTheNewCorrelationId()
    {
        var first = ReleasedOwnFleetShipment(1);
        var added = ReleasedOwnFleetShipment(2);
        var trip = CompleteDraft(first.Id);
        trip.Dispatch(new CorrelationId("c1"), DateTimeOffset.UtcNow, Truck, Joost, [new DispatchedShipment(first, TransportOrderTests.SampleDetails())]);
        trip.ClearDomainEvents();
        trip.AddShipment(added.Id);
        var at = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

        trip.Amend(new CorrelationId("c2"), at, Truck, Joost, [new DispatchedShipment(first, TransportOrderTests.SampleDetails()), new DispatchedShipment(added, TransportOrderTests.SampleDetails())]);

        trip.Version.Should().Be(2);
        trip.Status.Should().Be(TripStatus.Dispatched);
        trip.Deliveries.Select(delivery => (delivery.Version, delivery.State)).Should().Equal((1, TripDeliveryState.Sending), (2, TripDeliveryState.Sending));
        added.Status.Should().Be(ShipmentStatus.Dispatched);
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.Trip.Version.Should().Be(2);
        raised.CorrelationId.Value.Should().Be("c2");
        raised.DispatchedAt.Should().Be(at);
        raised.Shipments.Should().HaveCount(2);
    }

    [Fact]
    public void Amend_ADraftOrATripLackingAPart_IsABug()
    {
        var draft = CompleteDraft(ShipmentId.New());
        draft.Invoking(t => t.Amend(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, [])).Should().Throw<InvalidOperationException>();

        var trip = CompleteDraft(ShipmentId.New());
        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);
        trip.Plan(null, null);
        trip.Invoking(t => t.Amend(new CorrelationId("c2"), DateTimeOffset.UtcNow, Truck, Joost, [])).Should().Throw<InvalidOperationException>();
        trip.Version.Should().Be(1);
    }

    [Theory]
    [InlineData("TR-0000042", true)]
    [InlineData("TR-42", false)]
    [InlineData("SH-0000042", false)]
    public void TripNumber_TryParse_AcceptsOnlyTheFormat(string text, bool valid) =>
        TripNumber.TryParse(text, out _).Should().Be(valid);

    [Fact]
    public void AddShipment_PutsItsCollectionAndDeliveryStopsAtTheEnd_CollectionFirst()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));
        var first = ShipmentId.New();
        var second = ShipmentId.New();

        trip.AddShipment(first);
        trip.AddShipment(second);

        trip.Stops.Should().Equal(
            new TripStop(first, StopKind.Collection, 0),
            new TripStop(first, StopKind.Delivery, 1),
            new TripStop(second, StopKind.Collection, 2),
            new TripStop(second, StopKind.Delivery, 3));
        trip.HasShipment(first).Should().BeTrue();
        trip.HasShipment(ShipmentId.New()).Should().BeFalse();
        trip.ShipmentIds.Should().Equal(first, second);
    }

    [Fact]
    public void AddShipment_ThatIsOnTheTripAlready_ChangesNothing()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));
        var shipment = ShipmentId.New();
        trip.AddShipment(shipment);

        trip.AddShipment(shipment);

        trip.Stops.Should().HaveCount(2);
    }

    [Fact]
    public void AddShipment_OnATripUnderWay_IsABug()
    {
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null, new IdempotencyKey("k"));
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(dispatched, TripStatus.InProgress);

        dispatched.Invoking(trip => trip.AddShipment(ShipmentId.New())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TripsOfShipmentsSpec_FindsTheTripsThatCarryAnyOfTheShipments()
    {
        var wanted = ShipmentId.New();
        var carrying = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k1"));
        carrying.AddShipment(ShipmentId.New());
        carrying.AddShipment(wanted);
        var other = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null, new IdempotencyKey("k2"));
        other.AddShipment(ShipmentId.New());
        var empty = Trip.CreateDraft(TripNumber.FromSequence(3), Day, null, null, new IdempotencyKey("k3"));

        var found = new TripsOfShipmentsSpec([wanted]).Evaluate([carrying, other, empty]);

        found.Should().ContainSingle().Which.Should().BeSameAs(carrying);
    }

    [Fact]
    public void TheRefusals_NameTheShipmentAndTheTrip()
    {
        TripRefusal.ShipmentNotFound("SH-0000009").Message.Should().Contain("SH-0000009");
        TripRefusal.ShipmentNotReleased("SH-0000009", "it is Planned, not Released").Message.Should().Contain("SH-0000009").And.Contain("Planned");
        TripRefusal.ShipmentOnAnotherTrip("SH-0000009", TripNumber.FromSequence(4)).Message.Should().Contain("SH-0000009").And.Contain("TR-0000004");
        TripRefusal.CollectionDayMismatch("SH-0000009", new DateOnly(2026, 10, 15), Day).Message.Should().Contain("2026-10-15").And.Contain("2026-10-14");
        TripRefusal.CollectionDayMismatch("SH-0000009", null, Day).Message.Should().Contain("no known day");
    }

    [Fact]
    public void ReorderStops_PutsTheStopsInTheNewOrder_WithPositionsFromZero()
    {
        var (trip, first, second) = TripWithTwoShipments();

        var fault = trip.ReorderStops(
            [(second, StopKind.Collection), (first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Delivery)]);

        fault.Should().BeNull();
        trip.Stops.Select(stop => (stop.ShipmentId, stop.Kind, stop.Position)).Should().Equal(
            (second, StopKind.Collection, 0), (first, StopKind.Collection, 1), (first, StopKind.Delivery, 2), (second, StopKind.Delivery, 3));
    }

    [Fact]
    public void ReorderStops_ADeliveryBeforeItsCollection_IsRefusedWithCollectionBeforeDelivery_AndTheOrderIsUnchanged()
    {
        var (trip, first, second) = TripWithTwoShipments();
        var before = trip.Stops.ToList();

        var fault = trip.ReorderStops(
            [(first, StopKind.Delivery), (first, StopKind.Collection), (second, StopKind.Collection), (second, StopKind.Delivery)]);

        fault.Should().Be(new StopOrderFault(TripRefusalCode.CollectionBeforeDelivery, first));
        trip.Stops.Should().Equal(before);
    }

    [Fact]
    public void ReorderStops_AnOrderThatIsNotTheTripsStops_IsRefusedWithStopsNotTheTripsStops()
    {
        var (trip, first, second) = TripWithTwoShipments();
        var before = trip.Stops.ToList();

        trip.ReorderStops([(first, StopKind.Collection), (first, StopKind.Delivery)])!.Code.Should().Be(TripRefusalCode.StopsNotTheTripsStops);
        trip.ReorderStops(
            [(first, StopKind.Collection), (first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Delivery)])!.Code
            .Should().Be(TripRefusalCode.StopsNotTheTripsStops);
        trip.ReorderStops(
            [(first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Collection), (ShipmentId.New(), StopKind.Delivery)])!.Code
            .Should().Be(TripRefusalCode.StopsNotTheTripsStops);
        trip.Stops.Should().Equal(before);
    }

    [Fact]
    public void ReorderStops_OnATripUnderWay_IsABug()
    {
        var (trip, first, second) = TripWithTwoShipments();
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(trip, TripStatus.InProgress);

        trip.Invoking(t => t.ReorderStops([(first, StopKind.Collection), (first, StopKind.Delivery), (second, StopKind.Collection), (second, StopKind.Delivery)]))
            .Should().Throw<InvalidOperationException>();
    }

    private static (Trip Trip, ShipmentId First, ShipmentId Second) TripWithTwoShipments()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));
        var (first, second) = (ShipmentId.New(), ShipmentId.New());
        trip.AddShipment(first);
        trip.AddShipment(second);
        return (trip, first, second);
    }

    [Fact]
    public void MissingParts_OfAnEmptyDraft_NamesEachPartDispatchNeeds()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));

        trip.MissingParts().Should().Equal("a Vehicle", "a Driver", "a planned start", "a planned driving duration", "at least one Shipment");
    }

    [Fact]
    public void MissingParts_OfACompleteDraft_IsEmpty()
    {
        var trip = CompleteDraft(ShipmentId.New());

        trip.MissingParts().Should().BeEmpty();
    }

    [Fact]
    public void Dispatch_ACompleteDraft_MakesItDispatchedAtVersion1_AndRaisesOneTripDispatched()
    {
        var shipment = ShipmentId.New();
        var trip = CompleteDraft(shipment);
        var correlation = new CorrelationId("corr-dispatch");
        var at = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

        trip.Dispatch(correlation, at, Truck, Joost, []);

        trip.Status.Should().Be(TripStatus.Dispatched);
        trip.Version.Should().Be(1);
        var raised = trip.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TripDispatchedEvent>().Subject;
        raised.Trip.Should().BeSameAs(trip);
        raised.CorrelationId.Should().Be(correlation);
        raised.DispatchedAt.Should().Be(at);
        raised.Vehicle.Should().Be(Truck);
        raised.Driver.Should().Be(Joost);
    }

    [Fact]
    public void Dispatch_MakesEachOfItsShipmentsDispatched_AndAShipmentNotOnTheTripIsABug()
    {
        var onTrip = ReleasedOwnFleetShipment(1);
        var other = ReleasedOwnFleetShipment(2);
        var trip = CompleteDraft(onTrip.Id);
        var corr = new CorrelationId("c");

        trip.Invoking(t => t.Dispatch(corr, DateTimeOffset.UtcNow, Truck, Joost, [new DispatchedShipment(other, TransportOrderTests.SampleDetails())]))
            .Should().Throw<InvalidOperationException>();

        trip.Dispatch(corr, DateTimeOffset.UtcNow, Truck, Joost, [new DispatchedShipment(onTrip, TransportOrderTests.SampleDetails())]);
        onTrip.Status.Should().Be(ShipmentStatus.Dispatched);
    }

    [Fact]
    public void Dispatch_AnIncompleteDraft_IsABug()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null, new IdempotencyKey("k"));

        trip.Invoking(t => t.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, [])).Should().Throw<InvalidOperationException>();
        trip.Status.Should().Be(TripStatus.Draft);
    }

    [Fact]
    public void Dispatch_ADispatchedTrip_IsABug()
    {
        var trip = CompleteDraft(ShipmentId.New());
        trip.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, []);

        trip.Invoking(t => t.Dispatch(new CorrelationId("c"), DateTimeOffset.UtcNow, Truck, Joost, [])).Should().Throw<InvalidOperationException>();
    }

    private static readonly Vehicle Truck = new(new VehicleId("VEH-0002"), "TT-201-R", VehicleType.RigidTruck, 8000, 18, true);

    private static readonly Driver Joost = new(new DriverId("DRV-0001"), "Joost Bakker", new HashSet<LicenceCategory> { LicenceCategory.C }, true);

    private static Shipment ReleasedOwnFleetShipment(long sequence)
    {
        var at = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var order = TransportOrder.Accept(
            new ShipperId("SHP-0001"), TransportOrderTests.SampleDetails(), TransportOrderNumber.FromSequence(sequence), IntakeChannel.ShipperApi, new CorrelationId("i"), at);
        var shipment = order.Plan(ShipmentNumber.FromSequence(sequence), CarriedBy.OwnFleet, new IdempotencyKey("p"), new CorrelationId("p"), at)!;
        shipment.Release(order, new IdempotencyKey("r"), new CorrelationId("r"), at);
        return shipment;
    }

    private static Trip CompleteDraft(ShipmentId shipment)
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, Truck.Id, Joost.Id, new IdempotencyKey("k"));
        trip.Plan(new TimeOnly(7, 30), TimeSpan.FromHours(6));
        trip.AddShipment(shipment);
        return trip;
    }
}
