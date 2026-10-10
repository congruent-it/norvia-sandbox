using Norvia.Tms.Domain.Fleet;
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
}
