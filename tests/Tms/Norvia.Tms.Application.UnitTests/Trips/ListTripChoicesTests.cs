using Norvia.Tms.Application.Trips.Choices;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class ListTripChoicesTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_OffersTheVehiclesAndDriversAvailableThatDay_ThatHoldNoOtherTrip()
    {
        _fixture.Existing(TripsFixture.Tuesday, Van, null);
        _fixture.Planning.Day(TripsFixture.Tuesday).TakeOffTheRoad(Truck);

        var choices = await Choices(TripsFixture.Tuesday, forTrip: null);

        choices.Vehicles.Should().BeEmpty("the Van holds a Trip and the Truck is off the road");
        choices.Drivers.Select(driver => driver.Id).Should().BeEquivalentTo("DRV-0001", "DRV-0002");
    }

    [Fact]
    public async Task Handle_ForATrip_KeepsWhatThatTripHolds()
    {
        var trip = _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);

        var choices = await Choices(TripsFixture.Tuesday, trip.Number.Value);

        choices.Vehicles.Select(vehicle => vehicle.Id).Should().Contain("VEH-0002");
        choices.Drivers.Select(driver => driver.Id).Should().Contain("DRV-0001");
    }

    [Fact]
    public async Task Handle_ADriverWithoutAShiftThatDay_IsNotOffered()
    {
        var choices = await Choices(new DateOnly(2026, 10, 17), forTrip: null);

        choices.Drivers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ADayThatIsNotOpen_OffersNothing()
    {
        var choices = await Choices(new DateOnly(2026, 12, 1), forTrip: null);

        choices.Vehicles.Should().BeEmpty();
        choices.Drivers.Should().BeEmpty();
    }

    private async Task<TripChoices> Choices(DateOnly day, string? forTrip) =>
        await new ListTripChoicesHandler(_fixture.Planning.Days.AsRead(), _fixture.Trips.AsRead(), _fixture.Planning.Vehicles, _fixture.Planning.Drivers)
            .Handle(new ListTripChoicesQuery(day, forTrip), CancellationToken.None);
}
