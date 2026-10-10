using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.List;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Trips;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

public sealed class ListTripsTests
{
    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task Handle_ListsEveryTripByDayWithItsVehicleAndDriver_AlsoOneOfADayThatHasPassed()
    {
        _fixture.Existing(TripsFixture.Tuesday.AddDays(1), null, null);
        _fixture.Existing(TripsFixture.Tuesday, Van, Anouk);
        _fixture.Existing(Monday.AddDays(-1), null, null);

        var trips = await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.Planning.Vehicles, _fixture.Planning.Drivers)
            .Handle(new ListTripsQuery(), CancellationToken.None);

        trips.Select(trip => trip.Day).Should().Equal(Monday.AddDays(-1), TripsFixture.Tuesday, TripsFixture.Tuesday.AddDays(1));
        trips[1].Should().Be(new TripView("TR-0000101", TripsFixture.Tuesday, TripStatus.Draft, "VEH-0002", "VV-102-B", "DRV-0001", "DRV-0001", null, null));
        trips[0].VehicleId.Should().BeNull();
        trips[0].DriverName.Should().BeNull();
    }
}
