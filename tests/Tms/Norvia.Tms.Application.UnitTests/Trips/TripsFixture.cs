using Ardalis.SharedKernel;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.Trips;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Trips;

/// <summary>
/// Today is Monday 12 October 2026 with fifteen open days, as in <see cref="PlanningEditsFixture"/>. Anouk and Joost hold a
/// licence for the van only, so the truck gives LicenceNotHeld.
/// </summary>
internal sealed class TripsFixture
{
    public static readonly DateOnly Tuesday = new(2026, 10, 13);

    private long _sequence;

    public TripsFixture()
    {
        Planning.OpenDays();
        Trips = InMemoryRepository.Over(Stored);
        Numbers.NextAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(TripNumber.FromSequence(++_sequence)));
        var plannedDriving = Substitute.For<IPlannedDrivingHours>();
        plannedDriving.ListAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<PlannedDriving>());
        var rules = new TripAvailabilityRules(Planning.Days.AsRead(), Planning.Vehicles, Planning.Drivers, plannedDriving, DrivingHourLimits.Default, Planning.Horizon, Planning.Time);
        Checks = new TripPlanningChecks(Trips.AsRead(), rules);
    }

    public PlanningEditsFixture Planning { get; } = new();

    public List<Trip> Stored { get; } = [];

    public IRepository<Trip> Trips { get; }

    public ITripNumbers Numbers { get; } = Substitute.For<ITripNumbers>();

    public TripPlanningChecks Checks { get; }

    public Trip Existing(DateOnly day, VehicleId? vehicle, DriverId? driver)
    {
        var trip = Trip.CreateDraft(TripNumber.FromSequence(100 + Stored.Count), day, vehicle, driver);
        Stored.Add(trip);
        return trip;
    }
}
