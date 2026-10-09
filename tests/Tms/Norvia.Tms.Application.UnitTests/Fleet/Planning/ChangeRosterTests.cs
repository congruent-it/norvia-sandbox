using Ardalis.Result;
using Norvia.Tms.Application.Fleet.Planning.ChangeRoster;
using Norvia.Tms.Domain.Fleet;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

public sealed class ChangeRosterTests
{
    private static readonly IReadOnlyDictionary<DayOfWeek, ShiftTimes> Early = Enum.GetValues<DayOfWeek>()
        .Where(day => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        .ToDictionary(day => day, _ => new ShiftTimes(new TimeOnly(4, 0), new TimeOnly(12, 0)));

    private readonly PlanningEditsFixture _fixture = new();

    [Fact]
    public async Task Handle_RewritesEveryOpenDayThatIsntEditedByHandAndHasNoAbsence()
    {
        _fixture.OpenDays();
        var edited = Monday.AddDays(1);
        _fixture.Day(edited).EditShift(Anouk, new TimeOnly(9, 0), new TimeOnly(17, 0));
        var absent = Monday.AddDays(2);
        _fixture.StoredAbsences.Add(Absence.Record(Anouk, absent, AbsenceReason.Leave, null));
        _fixture.Day(absent).RecordAbsence(Anouk);

        var result = await Run(Anouk, Early);

        result.IsSuccess.Should().BeTrue();
        _fixture.Day(Monday).Shifts.Single(shift => shift.DriverId == Anouk).Start.Should().Be(new TimeOnly(4, 0));
        _fixture.Day(edited).Shifts.Single(shift => shift.DriverId == Anouk).Should().Be(new Shift(Anouk, new TimeOnly(9, 0), new TimeOnly(17, 0), true));
        _fixture.Day(absent).Shifts.Should().NotContain(shift => shift.DriverId == Anouk);
        _fixture.Day(Monday.AddDays(3)).Shifts.Single(shift => shift.DriverId == Anouk).Start.Should().Be(new TimeOnly(4, 0));
        _fixture.Day(Monday).Shifts.Single(shift => shift.DriverId == Joost).Start.Should().Be(new TimeOnly(8, 0));
        result.Value.DaysRewritten.Should().Be(13, "every open day but the one edited by hand and the one with an Absence");
    }

    [Fact]
    public async Task Handle_TheRosterIsKeptAndWinsOverTheSeededOne()
    {
        _fixture.OpenDays();

        await Run(Anouk, Early);
        await Run(Anouk, Early);

        _fixture.StoredRosters.Should().ContainSingle().Which.ShiftOn(Monday)!.Start.Should().Be(new TimeOnly(4, 0));
        (await _fixture.Rosters.FindAsync(Anouk, CancellationToken.None))!.ShiftOn(Monday)!.Start.Should().Be(new TimeOnly(4, 0));
    }

    [Fact]
    public async Task Handle_AWeekdayLeftOut_BecomesADayOff()
    {
        _fixture.OpenDays();

        await Run(Anouk, new Dictionary<DayOfWeek, ShiftTimes> { [DayOfWeek.Tuesday] = new(new TimeOnly(6, 0), new TimeOnly(14, 0)) });

        _fixture.Day(Monday).Shifts.Should().NotContain(shift => shift.DriverId == Anouk);
        _fixture.Day(Monday.AddDays(1)).Shifts.Should().Contain(shift => shift.DriverId == Anouk);
    }

    [Fact]
    public async Task Handle_DaysBeforeTodayThatAreStillOpen_KeepTheirShifts()
    {
        _fixture.OpenDays();
        var yesterday = PlanningDay.Open(Monday.AddDays(-3), _fixture.SeededRosters.List(), []);
        _fixture.StoredDays.Add(yesterday);

        await Run(Anouk, Early);

        yesterday.Shifts.Single(shift => shift.DriverId == Anouk).Start.Should().Be(new TimeOnly(6, 0));
    }

    [Fact]
    public async Task Handle_AnUnknownDriver_IsRefused()
    {
        var result = await Run(new DriverId("DRV-9999"), Early);

        PlanningEditsFixture.Code(result).Should().Be(nameof(PlanningRefusalCode.DriverNotFound));
        _fixture.StoredRosters.Should().BeEmpty();
    }

    private async Task<Result<ChangeRosterResult>> Run(DriverId driver, IReadOnlyDictionary<DayOfWeek, ShiftTimes> weekdays) =>
        await new ChangeRosterHandler(_fixture.ChangedRosters, _fixture.Days, _fixture.Absences.AsRead(), _fixture.Drivers, _fixture.Horizon, _fixture.Time)
            .Handle(new ChangeRosterCommand(driver, weekdays), CancellationToken.None);
}
