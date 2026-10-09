using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Availability;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.UnitTests.Fleet.Planning;
using Norvia.Tms.Application.UnitTests.TransportOrders;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Application.UnitTests.Fleet.Availability;

public sealed class TripAvailabilityRulesTests
{
    // Thursday 24 Sep 2026: the horizon runs to Thursday 8 Oct.
    private static readonly DateOnly Friday = new(2026, 9, 25);
    private static readonly VehicleId TruckId = new("VEH-0001");
    private static readonly DriverId AnoukId = new("DRV-0001");

    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly List<PlanningDay> _days = [];
    private readonly List<Vehicle> _vehicles = [new(TruckId, "NL-01-TR", VehicleType.RigidTruck, 10000, 18, true)];
    private readonly List<Driver> _drivers = [new(AnoukId, "Anouk de Vries", new HashSet<LicenceCategory> { LicenceCategory.C }, true)];
    private readonly List<PlannedDriving> _planned = [];

    public TripAvailabilityRulesTests()
    {
        _days.Add(OpenDay(Friday));
    }

    [Fact]
    public async Task Check_AValidPairing_Passes()
    {
        (await CheckAsync(Valid())).Should().BeEmpty();
    }

    [Fact]
    public async Task Check_AVehicleOffTheRoad_IsVehicleNotAvailable()
    {
        _days[0] = PlanningDay.Open(Friday, [Roster()], [TruckId], offTheRoadVehicles: new HashSet<VehicleId> { TruckId });

        var failures = await CheckAsync(Valid());

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.VehicleNotAvailable, "Vehicle VEH-0001 is not available on 2026-09-25: it is off the road."));
    }

    [Fact]
    public async Task Check_AnInactiveVehicle_IsVehicleNotAvailable()
    {
        _vehicles[0] = _vehicles[0] with { Active = false };

        (await CheckAsync(Valid())).Should().ContainSingle().Which.Code.Should().Be(TripAvailabilityCode.VehicleNotAvailable);
    }

    [Fact]
    public async Task Check_AVehicleWithAnotherTrip_IsVehicleNotAvailable()
    {
        var failures = await CheckAsync(Valid() with { VehicleHasOtherTrip = true });

        failures.Should().ContainSingle().Which.Message.Should().Be("Vehicle VEH-0001 is not available on 2026-09-25: it has another Trip that day.");
    }

    [Fact]
    public async Task Check_ADriverWithAnAbsenceOrNoShift_IsDriverNotAvailable()
    {
        _days[0].RecordAbsence(AnoukId);

        var failures = await CheckAsync(Valid());

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.DriverNotAvailable, "Driver DRV-0001 is not available on 2026-09-25: they have no Shift that day."));
    }

    [Fact]
    public async Task Check_AnInactiveDriver_IsDriverNotAvailable()
    {
        _drivers[0] = _drivers[0] with { Active = false };

        (await CheckAsync(Valid())).Should().ContainSingle().Which.Code.Should().Be(TripAvailabilityCode.DriverNotAvailable);
    }

    [Fact]
    public async Task Check_ADriverWithAnotherTrip_IsDriverNotAvailable()
    {
        var failures = await CheckAsync(Valid() with { DriverHasOtherTrip = true });

        failures.Should().ContainSingle().Which.Message.Should().Be("Driver DRV-0001 is not available on 2026-09-25: they have another Trip that day.");
    }

    [Fact]
    public async Task Check_ALicenceThatDoesNotCoverTheVehicleType_IsLicenceNotHeld()
    {
        _drivers[0] = _drivers[0] with { Licences = new HashSet<LicenceCategory> { LicenceCategory.B } };

        var failures = await CheckAsync(Valid());

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.LicenceNotHeld, "Driver DRV-0001 does not hold a licence for Vehicle VEH-0001, which needs category C."));
    }

    [Fact]
    public async Task Check_ATripThatDoesNotFitTheDrivingHourBudget_IsDrivingHoursExceeded()
    {
        _planned.Add(new PlannedDriving(AnoukId, Friday, TimeSpan.FromHours(8)));

        var failures = await CheckAsync(Valid() with { DrivingDuration = TimeSpan.FromHours(3) });

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.DrivingHoursExceeded, "A planned driving duration of 3 h does not fit in Driver DRV-0001's Driving-hour budget."));
    }

    [Fact]
    public async Task Check_ATripUsingAnExtension_FitsTheBudget()
    {
        _planned.Add(new PlannedDriving(AnoukId, Friday, TimeSpan.FromHours(8)));

        (await CheckAsync(Valid() with { DrivingDuration = TimeSpan.FromHours(2) })).Should().BeEmpty();
    }

    [Fact]
    public async Task Check_AWeightOverThePayload_IsPayloadExceeded()
    {
        var failures = await CheckAsync(Valid() with { Load = new FleetLoad(10001m, 10) });

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.PayloadExceeded, "The Trip weighs 10001 kg, more than Vehicle VEH-0001 can carry (10000 kg)."));
    }

    [Fact]
    public async Task Check_MorePalletsThanPlaces_IsPalletPlacesExceeded()
    {
        var failures = await CheckAsync(Valid() with { Load = new FleetLoad(5000m, 19) });

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.PalletPlacesExceeded, "The Trip has 19 pallets, more than Vehicle VEH-0001 has places for (18)."));
    }

    [Fact]
    public async Task Check_ADayOutsideThePlanningHorizon_IsDayNotOpen()
    {
        var failures = await CheckAsync(Valid() with { Day = new DateOnly(2026, 10, 9) });

        failures.Should().ContainSingle().Which.Should().Be(
            new TripAvailabilityFailure(TripAvailabilityCode.DayNotOpen, "2026-10-09 is not in the Planning horizon."));
    }

    [Fact]
    public async Task Check_ADayBeforeToday_IsDayNotOpen()
    {
        (await CheckAsync(Valid() with { Day = new DateOnly(2026, 9, 23) })).Should().ContainSingle().Which.Code.Should().Be(TripAvailabilityCode.DayNotOpen);
    }

    [Fact]
    public async Task Check_ADraftWithoutVehicleDriverOrDuration_OnlyChecksWhatIsSet()
    {
        (await CheckAsync(new TripProposal(Friday, new FleetLoad(50000m, 99)))).Should().BeEmpty();
    }

    [Fact]
    public async Task Check_SeveralBrokenRules_ReportsEachOne()
    {
        _drivers[0] = _drivers[0] with { Licences = new HashSet<LicenceCategory> { LicenceCategory.B } };

        var failures = await CheckAsync(Valid() with { Load = new FleetLoad(20000m, 30), VehicleHasOtherTrip = true });

        failures.Select(failure => failure.Code).Should().Equal(
            TripAvailabilityCode.VehicleNotAvailable, TripAvailabilityCode.LicenceNotHeld, TripAvailabilityCode.PayloadExceeded, TripAvailabilityCode.PalletPlacesExceeded);
    }

    private static TripProposal Valid() => new(Friday, new FleetLoad(5000m, 10), TruckId, AnoukId, TimeSpan.FromHours(6));

    private static Roster Roster() =>
        new(AnoukId, Enum.GetValues<DayOfWeek>().ToDictionary(weekday => weekday, _ => new ShiftTimes(new TimeOnly(6, 0), new TimeOnly(14, 0))));

    private static PlanningDay OpenDay(DateOnly date) => PlanningDay.Open(date, [Roster()], [TruckId]);

    private Task<IReadOnlyList<TripAvailabilityFailure>> CheckAsync(TripProposal trip)
    {
        var drivers = Substitute.For<IDriverDirectory>();
        drivers.ListActive().Returns(_ => _drivers.Where(driver => driver.Active).ToList());
        var vehicles = Substitute.For<IVehicleDirectory>();
        vehicles.ListActive().Returns(_ => _vehicles.Where(vehicle => vehicle.Active).ToList());
        var plannedDriving = Substitute.For<IPlannedDrivingHours>();
        plannedDriving.ListAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_planned);
        var rules = new TripAvailabilityRules(
            LoadChecks.Repository(_days), vehicles, drivers, plannedDriving, DrivingHourLimits.Default, new PlanningHorizonRules(14, LoadChecks.Amsterdam), _time);

        return rules.CheckAsync(trip, CancellationToken.None);
    }
}
