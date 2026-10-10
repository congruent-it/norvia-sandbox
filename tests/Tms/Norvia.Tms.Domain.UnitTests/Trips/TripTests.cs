using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Domain.UnitTests.Trips;

public sealed class TripTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);

    [Fact]
    public void CreateDraft_WithoutVehicleAndDriver_IsAnEmptyDraftOnTheDay()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(7), Day, null, null);

        trip.Number.Value.Should().Be("TR-0000007");
        trip.Day.Should().Be(Day);
        trip.Status.Should().Be(TripStatus.Draft);
        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().BeNull();
    }

    [Fact]
    public void Pick_ChangesTheVehicleAndDriverOfADraft_AndNullGivesThemBack()
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(1), Day, new VehicleId("VEH-0001"), null);

        trip.Pick(new VehicleId("VEH-0002"), new DriverId("DRV-0001")).Should().BeNull();
        trip.VehicleId.Should().Be(new VehicleId("VEH-0002"));
        trip.DriverId.Should().Be(new DriverId("DRV-0001"));

        trip.Pick(null, null).Should().BeNull();
        trip.VehicleId.Should().BeNull();
        trip.DriverId.Should().BeNull();
    }

    [Fact]
    public void Discard_AllowsADraftAndRefusesADispatchedTrip()
    {
        var draft = Trip.CreateDraft(TripNumber.FromSequence(1), Day, null, null);
        var dispatched = Trip.CreateDraft(TripNumber.FromSequence(2), Day, null, null);
        // Dispatch isn't built yet (a later P11 slice), so put the Trip in the status it will have.
        typeof(Trip).GetProperty(nameof(Trip.Status))!.SetValue(dispatched, TripStatus.Dispatched);

        draft.EnsureDraft().Should().BeNull();
        dispatched.EnsureDraft()!.Code.Should().Be(TripRefusalCode.TripNotDraft);
        dispatched.Pick(null, null)!.Code.Should().Be(TripRefusalCode.TripNotDraft);
    }

    [Theory]
    [InlineData("TR-0000042", true)]
    [InlineData("TR-42", false)]
    [InlineData("SH-0000042", false)]
    public void TripNumber_TryParse_AcceptsOnlyTheFormat(string text, bool valid) =>
        TripNumber.TryParse(text, out _).Should().Be(valid);
}
