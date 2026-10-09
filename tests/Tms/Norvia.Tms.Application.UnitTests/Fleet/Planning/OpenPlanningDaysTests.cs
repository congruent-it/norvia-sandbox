using Ardalis.SharedKernel;
using Ardalis.Specification;
using Microsoft.Extensions.Time.Testing;
using Norvia.Tms.Application.Fleet;
using Norvia.Tms.Application.Fleet.Planning.OpenDays;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

/// <summary>The horizon job (P10): opens every missing day, fills Shifts from Rosters, closes the past, and is idempotent.</summary>
public sealed class OpenPlanningDaysTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DriverId Anouk = new("DRV-0001");
    private static readonly DriverId Joost = new("DRV-0002");

    private readonly List<PlanningDay> _stored = [];
    private readonly List<Roster> _changedRosters = [];
    private readonly List<Absence> _absences = [];
    private readonly List<OffTheRoadDay> _offTheRoad = [];
    private readonly IRepository<PlanningDay> _days;
    private readonly IRosterDirectory _rosters = Substitute.For<IRosterDirectory>();
    private readonly IDriverDirectory _drivers = Substitute.For<IDriverDirectory>();
    private readonly IVehicleDirectory _vehicles = Substitute.For<IVehicleDirectory>();
    private readonly FakeTimeProvider _time = new();

    public OpenPlanningDaysTests()
    {
        _days = InMemoryRepository.Over(_stored);
        _rosters.List().Returns([Roster(Anouk, 6, 14), Roster(Joost, 8, 16)]);
        _drivers.ListActive().Returns([Driver(Anouk), Driver(Joost)]);
        _vehicles.ListActive().Returns([new Vehicle(new VehicleId("VEH-0001"), "VN-101-A", VehicleType.Van, 1200, 4, true)]);
        SetLocalTime(Monday, 0, 0);
    }

    [Fact]
    public async Task Handle_OnAnEmptyHorizon_OpensTodayAndTheNextFourteenDays_WithShiftsFromTheRosters()
    {
        var result = await Run();

        result.Opened.Should().HaveCount(15);
        result.Opened[0].Should().Be(Monday);
        result.Opened[^1].Should().Be(Monday.AddDays(14));
        _stored.Should().HaveCount(15);
        var today = _stored.Single(day => day.Date == Monday);
        today.Shifts.Select(shift => (shift.DriverId, shift.Start)).Should().BeEquivalentTo([(Anouk, new TimeOnly(6, 0)), (Joost, new TimeOnly(8, 0))]);
        today.AvailableVehicles.Should().ContainSingle();
        _stored.Single(day => day.Date == Monday.AddDays(5)).Shifts.Should().BeEmpty("a Saturday is a day off in these Rosters");
    }

    [Fact]
    public async Task Handle_RunTwice_ChangesNothingTheSecondTime()
    {
        await Run();
        var shiftsBefore = _stored.ToDictionary(day => day.Date, day => day.Shifts);

        var second = await Run();

        second.Opened.Should().BeEmpty();
        second.Closed.Should().Be(0);
        _stored.Should().HaveCount(15);
        _stored.ToDictionary(day => day.Date, day => day.Shifts).Should().BeEquivalentTo(shiftsBefore);
    }

    [Fact]
    public async Task Handle_AfterDowntime_OpensEveryMissingDayUpToTheEndOfTheHorizon_AndClosesThePast()
    {
        await Run();
        SetLocalTime(Monday.AddDays(5), 0, 5);

        var result = await Run();

        result.Opened.Should().Equal(Enumerable.Range(15, 5).Select(Monday.AddDays), "the days after the old horizon, up to the new end");
        result.Closed.Should().Be(5);
        _stored.Where(day => day.Date < Monday.AddDays(5)).Should().OnlyContain(day => day.Status == PlanningDayStatus.Closed);
        _stored.Where(day => day.Date >= Monday.AddDays(5)).Should().OnlyContain(day => day.Status == PlanningDayStatus.Open);
        _stored.Select(day => day.Date).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_AfterALongDowntime_DoesNotOpenTheDaysThatPassedWhileDown()
    {
        await Run();
        SetLocalTime(Monday.AddDays(40), 9, 0);

        var result = await Run();

        result.Opened.Should().HaveCount(15);
        result.Opened[0].Should().Be(Monday.AddDays(40));
    }

    [Fact]
    public async Task Handle_ADriverWhoseRosterWorksThatWeekdayButIsInactive_GetsNoShift()
    {
        _drivers.ListActive().Returns([Driver(Anouk)]);

        await Run();

        _stored.Single(day => day.Date == Monday).Shifts.Should().ContainSingle().Which.DriverId.Should().Be(Anouk);
    }

    [Fact]
    public async Task Handle_JustBeforeMidnightInAmsterdam_OpensNothingNew()
    {
        await Run();
        SetLocalTime(Monday, 23, 59);

        var result = await Run();

        result.Opened.Should().BeEmpty();
        result.Closed.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AnAbsenceBeyondTheHorizonEdge_TakesEffectWhenThatDayOpens()
    {
        var farDay = Monday.AddDays(22);
        _absences.Add(Absence.Record(Anouk, farDay, AbsenceReason.Leave, null));
        await Run();
        _stored.Should().NotContain(day => day.Date == farDay);

        SetLocalTime(Monday.AddDays(8), 0, 0);
        await Run();

        _stored.Single(day => day.Date == farDay).Shifts.Select(shift => shift.DriverId).Should().Equal(Joost);
    }

    [Fact]
    public async Task Handle_AnOffTheRoadDay_KeepsTheVehicleOutOfTheDayItOpens()
    {
        _offTheRoad.Add(OffTheRoadDay.Record(new VehicleId("VEH-0001"), Monday.AddDays(2), OffTheRoadReason.Maintenance, null));

        await Run();

        _stored.Single(day => day.Date == Monday.AddDays(2)).AvailableVehicles.Should().BeEmpty();
        _stored.Single(day => day.Date == Monday.AddDays(1)).AvailableVehicles.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AChangedRoster_IsUsedInsteadOfTheSeededOne()
    {
        _changedRosters.Add(new Roster(Anouk, new Dictionary<DayOfWeek, ShiftTimes> { [DayOfWeek.Monday] = new(new TimeOnly(5, 0), new TimeOnly(13, 0)) }));

        await Run();

        _stored.Single(day => day.Date == Monday).Shifts.Single(shift => shift.DriverId == Anouk).Start.Should().Be(new TimeOnly(5, 0));
        _stored.Single(day => day.Date == Monday.AddDays(1)).Shifts.Should().NotContain(shift => shift.DriverId == Anouk);
    }

    private async Task<OpenPlanningDaysResult> Run() =>
        await new OpenPlanningDaysHandler(_days, new EffectiveRosters(_rosters, InMemoryRepository.Over(_changedRosters).AsRead()), InMemoryRepository.Over(_absences).AsRead(), InMemoryRepository.Over(_offTheRoad).AsRead(), _drivers, _vehicles, new PlanningHorizonRules(14, Amsterdam), _time)
            .Handle(new OpenPlanningDaysCommand(), CancellationToken.None);

    private void SetLocalTime(DateOnly date, int hour, int minute)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        _time.SetUtcNow(new DateTimeOffset(local, Amsterdam.GetUtcOffset(local)));
    }

    private static Roster Roster(DriverId driver, int startHour, int endHour) => new(
        driver,
        Enum.GetValues<DayOfWeek>().Where(day => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToDictionary(day => day, _ => new ShiftTimes(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0))));

    private static Driver Driver(DriverId id) => new(id, id.Value, new HashSet<LicenceCategory> { LicenceCategory.B }, Active: true);
}
