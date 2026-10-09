using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

/// <summary>Today is Monday 12 October 2026, 09:00 in Amsterdam; two active Drivers and two active Vehicles.</summary>
internal sealed class PlanningEditsFixture
{
    public static readonly DateOnly Monday = new(2026, 10, 12);
    public static readonly DriverId Anouk = new("DRV-0001");
    public static readonly DriverId Joost = new("DRV-0002");
    public static readonly VehicleId Truck = new("VEH-0001");
    public static readonly VehicleId Van = new("VEH-0002");

    public PlanningEditsFixture()
    {
        Drivers.ListActive().Returns([Driver(Anouk), Driver(Joost)]);
        Vehicles.ListActive().Returns(
        [
            new Vehicle(Truck, "VT-101-A", VehicleType.RigidTruck, 10000, 18, true),
            new Vehicle(Van, "VV-102-B", VehicleType.Van, 1200, 4, true),
        ]);
        SeededRosters.List().Returns([WeekdayRoster(Anouk, 6, 14), WeekdayRoster(Joost, 8, 16)]);
        Days = InMemoryRepository.Over(StoredDays);
        Absences = InMemoryRepository.Over(StoredAbsences);
        OffTheRoadDays = InMemoryRepository.Over(StoredOffTheRoadDays);
        ChangedRosters = InMemoryRepository.Over(StoredRosters);
        Time.SetUtcNow(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(2)));
    }

    public List<PlanningDay> StoredDays { get; } = [];

    public List<Absence> StoredAbsences { get; } = [];

    public List<OffTheRoadDay> StoredOffTheRoadDays { get; } = [];

    public List<Roster> StoredRosters { get; } = [];

    public IRepository<PlanningDay> Days { get; }

    public IRepository<Absence> Absences { get; }

    public IRepository<OffTheRoadDay> OffTheRoadDays { get; }

    public IRepository<Roster> ChangedRosters { get; }

    public IDriverDirectory Drivers { get; } = Substitute.For<IDriverDirectory>();

    public IVehicleDirectory Vehicles { get; } = Substitute.For<IVehicleDirectory>();

    public IRosterDirectory SeededRosters { get; } = Substitute.For<IRosterDirectory>();

    public FakeTimeProvider Time { get; } = new();

    public PlanningHorizonRules Horizon { get; } = new(14, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));

    public EffectiveRosters Rosters => new(SeededRosters, ChangedRosters.AsRead());

    /// <summary>Opens today and the next days from the seeded Rosters, as the horizon job does.</summary>
    public void OpenDays(int count = 15)
    {
        foreach (var offset in Enumerable.Range(0, count))
        {
            StoredDays.Add(PlanningDay.Open(Monday.AddDays(offset), SeededRosters.List(), [Truck, Van]));
        }
    }

    public PlanningDay Day(DateOnly date) => StoredDays.Single(day => day.Date == date);

    public static Roster WeekdayRoster(DriverId driver, int startHour, int endHour) => new(
        driver,
        Enum.GetValues<DayOfWeek>().Where(day => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToDictionary(day => day, _ => new ShiftTimes(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0))));

    public static string Code(Ardalis.Result.IResult result) => result.ValidationErrors.Single().ErrorCode;

    private static Driver Driver(DriverId id) => new(id, id.Value, new HashSet<LicenceCategory> { LicenceCategory.B }, Active: true);
}
