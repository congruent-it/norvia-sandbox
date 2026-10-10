using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;

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
    public void Plan_OnADispatchedTrip_IsABug()
    {
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null, new IdempotencyKey("k"));
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(dispatched, TripStatus.Dispatched);

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
        dispatched.Invoking(trip => trip.Pick(null, null)).Should().Throw<InvalidOperationException>();
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
    public void AddShipment_OnADispatchedTrip_IsABug()
    {
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null, new IdempotencyKey("k"));
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(dispatched, TripStatus.Dispatched);

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
}
