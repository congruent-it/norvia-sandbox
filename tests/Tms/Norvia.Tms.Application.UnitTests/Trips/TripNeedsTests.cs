using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Application.Fleet.Planning.ChangeRoster;
using Norvia.Tms.Application.Fleet.Planning.RecordAbsence;
using Norvia.Tms.Application.Fleet.Planning.RecordOffTheRoad;
using Norvia.Tms.Application.Fleet.Planning.RemoveAbsence;
using Norvia.Tms.Application.Fleet.Planning.RemoveOffTheRoad;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.GetByNumber;
using Norvia.Tms.Application.Trips.List;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Domain.Trips;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>The Needs a Driver and Needs a Vehicle flags (P11): computed from the Trip and P10's availability, and never a change to the Trip.</summary>
public sealed class TripNeedsTests
{
    private static readonly DateOnly Day = TripsFixture.Tuesday;

    private readonly TripsFixture _fixture = new();

    [Fact]
    public async Task ATripWhoseDriverAndVehicleAreAvailable_IsNotFlagged()
    {
        var trip = _fixture.Existing(Day, Van, Anouk);

        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(TripNeeds.None);
    }

    [Fact]
    public async Task AnAbsence_FlagsNeedsADriver_AndRemovingItClearsTheFlag()
    {
        var trip = _fixture.Existing(Day, Van, Anouk);
        await new RecordAbsenceHandler(_fixture.Planning.Absences, _fixture.Planning.Days, _fixture.Planning.Drivers, _fixture.Planning.Horizon, _fixture.Planning.Time)
            .Handle(new RecordAbsenceCommand(Anouk, Day, Day, AbsenceReason.Sickness, null), CancellationToken.None);

        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(new TripNeeds(Driver: true, Vehicle: false));

        await new RemoveAbsenceHandler(_fixture.Planning.Absences, _fixture.Planning.Days, _fixture.Planning.Rosters, _fixture.Planning.Horizon, _fixture.Planning.Time)
            .Handle(new RemoveAbsenceCommand(_fixture.Planning.StoredAbsences.Single().Id), CancellationToken.None);
        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(TripNeeds.None);
    }

    [Fact]
    public async Task AnOffTheRoadDay_FlagsNeedsAVehicle_AndRemovingItClearsTheFlag()
    {
        var trip = _fixture.Existing(Day, Van, Anouk);
        await new RecordOffTheRoadHandler(_fixture.Planning.OffTheRoadDays, _fixture.Planning.Days, _fixture.Planning.Vehicles, _fixture.Planning.Horizon, _fixture.Planning.Time)
            .Handle(new RecordOffTheRoadCommand(Van, Day, Day, OffTheRoadReason.Repair, null), CancellationToken.None);

        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(new TripNeeds(Driver: false, Vehicle: true));

        await new RemoveOffTheRoadHandler(_fixture.Planning.OffTheRoadDays, _fixture.Planning.Days, _fixture.Planning.Horizon, _fixture.Planning.Time)
            .Handle(new RemoveOffTheRoadCommand(_fixture.Planning.StoredOffTheRoadDays.Single().Id), CancellationToken.None);
        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(TripNeeds.None);
    }

    [Fact]
    public async Task ARosterChangeThatNoLongerCoversTheDay_FlagsNeedsADriver_AndRestoringTheRosterClearsTheFlag()
    {
        var trip = _fixture.Existing(Day, Van, Anouk);
        var handler = new ChangeRosterHandler(_fixture.Planning.ChangedRosters, _fixture.Planning.Days, _fixture.Planning.Absences.AsRead(), _fixture.Planning.Drivers, _fixture.Planning.Horizon, _fixture.Planning.Time);
        var wednesdayOnly = new Dictionary<DayOfWeek, ShiftTimes> { [DayOfWeek.Wednesday] = new(new TimeOnly(6, 0), new TimeOnly(14, 0)) };
        await handler.Handle(new ChangeRosterCommand(Anouk, wednesdayOnly), CancellationToken.None);

        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(new TripNeeds(Driver: true, Vehicle: false));

        await handler.Handle(new ChangeRosterCommand(Anouk, WeekdayRoster(Anouk, 6, 14).Weekdays), CancellationToken.None);
        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(TripNeeds.None);
    }

    [Fact]
    public async Task ADriverOrVehicleThatBecameInactive_FlagsTheTrip_AndBothCanBeFlaggedAtOnce()
    {
        var trip = _fixture.Existing(Day, Van, Anouk);
        _fixture.Planning.Drivers.ListActive().Returns([]);
        _fixture.Planning.Vehicles.ListActive().Returns([]);

        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(new TripNeeds(Driver: true, Vehicle: true));
    }

    [Fact]
    public async Task ADraftWithoutADriverOrVehicle_IsNotFlagged_AndAnotherTripThatDayDoesNotFlag()
    {
        var bare = _fixture.Existing(Day, null, null);
        var first = _fixture.Existing(Day, Van, Anouk);
        _fixture.Existing(Day, Van, Anouk);

        (await _fixture.NeedsCheck.OfAsync(bare, CancellationToken.None)).Should().Be(TripNeeds.None);
        (await _fixture.NeedsCheck.OfAsync(first, CancellationToken.None)).Should().Be(TripNeeds.None);
    }

    [Fact]
    public async Task AFlag_ChangesNothingOnTheDispatchedTrip_AndTheTripsViewAndDetailShowIt()
    {
        var trip = await _fixture.DispatchedAsync();
        var versionBefore = (trip.Version, trip.Status, trip.DriverId, trip.VehicleId);
        await new RecordAbsenceHandler(_fixture.Planning.Absences, _fixture.Planning.Days, _fixture.Planning.Drivers, _fixture.Planning.Horizon, _fixture.Planning.Time)
            .Handle(new RecordAbsenceCommand(Anouk, Day, Day, AbsenceReason.Sickness, null), CancellationToken.None);

        var listed = await new ListTripsHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck)
            .Handle(new ListTripsQuery(), CancellationToken.None);
        var detail = await new GetTripHandler(_fixture.Trips.AsRead(), _fixture.TripShipments, _fixture.Checks, _fixture.Planning.Vehicles, _fixture.Planning.Drivers, _fixture.NeedsCheck)
            .Handle(new GetTripQuery(trip.Number.Value), CancellationToken.None);

        listed.Single().Should().Match<TripView>(view => view.NeedsDriver && !view.NeedsVehicle);
        detail.Value.Trip.Should().Match<TripView>(view => view.NeedsDriver && !view.NeedsVehicle);
        (trip.Version, trip.Status, trip.DriverId, trip.VehicleId).Should().Be(versionBefore);
        trip.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ATripThatIsInProgress_IsNotFlagged()
    {
        var trip = await _fixture.DispatchedAsync();
        trip.Start(new CorrelationId("corr-start"), DateTimeOffset.UtcNow);
        _fixture.Planning.Drivers.ListActive().Returns([]);

        (await _fixture.NeedsCheck.OfAsync(trip, CancellationToken.None)).Should().Be(TripNeeds.None);
    }
}
