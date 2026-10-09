using Norvia.Tms.Application.Fleet.Planning.ListAbsences;
using Norvia.Tms.Application.Fleet.Planning.ListOffTheRoadDays;
using Norvia.Tms.Application.Fleet.Planning.ListRosters;
using Norvia.Tms.Domain.Fleet;
using NSubstitute;
using static Norvia.Tms.Application.UnitTests.Fleet.Planning.PlanningEditsFixture;

namespace Norvia.Tms.Application.UnitTests.Fleet.Planning;

/// <summary>What the Drivers and Vehicles tabs list: the Absences, Off the road days and Rosters.</summary>
public sealed class ListPlanningRecordsTests
{
    private readonly PlanningEditsFixture _fixture = new();

    [Fact]
    public async Task ListAbsences_ListsTodayAndLaterByDate_WithTheDriversName()
    {
        _fixture.StoredAbsences.AddRange(
        [
            Absence.Record(Joost, Monday.AddDays(5), AbsenceReason.Training, "ADR"),
            Absence.Record(Anouk, Monday.AddDays(2), AbsenceReason.Leave, null),
            Absence.Record(Anouk, Monday.AddDays(-1), AbsenceReason.Sickness, null),
            Absence.Record(new DriverId("DRV-9999"), Monday, AbsenceReason.Leave, null),
        ]);

        var absences = await new ListAbsencesHandler(_fixture.Absences.AsRead(), _fixture.Drivers, _fixture.Horizon, _fixture.Time)
            .Handle(new ListAbsencesQuery(), CancellationToken.None);

        absences.Select(absence => (absence.Date, absence.DriverName, absence.Reason, absence.Remark))
            .Should().Equal(
                (Monday.AddDays(2), "DRV-0001", AbsenceReason.Leave, string.Empty),
                (Monday.AddDays(5), "DRV-0002", AbsenceReason.Training, "ADR"));
    }

    [Fact]
    public async Task ListOffTheRoadDays_ListsTodayAndLaterByDate_WithTheRegistration()
    {
        _fixture.StoredOffTheRoadDays.AddRange(
        [
            OffTheRoadDay.Record(Van, Monday.AddDays(3), OffTheRoadReason.Repair, "Brakes"),
            OffTheRoadDay.Record(Truck, Monday, OffTheRoadReason.Maintenance, null),
            OffTheRoadDay.Record(Truck, Monday.AddDays(-4), OffTheRoadReason.Maintenance, null),
        ]);

        var days = await new ListOffTheRoadDaysHandler(_fixture.OffTheRoadDays.AsRead(), _fixture.Vehicles, _fixture.Horizon, _fixture.Time)
            .Handle(new ListOffTheRoadDaysQuery(), CancellationToken.None);

        days.Select(day => (day.Date, day.Registration, day.Reason)).Should().Equal(
            (Monday, "VT-101-A", OffTheRoadReason.Maintenance),
            (Monday.AddDays(3), "VV-102-B", OffTheRoadReason.Repair));
    }

    [Fact]
    public async Task ListRosters_ShowsEachActiveDriversWeekdaysMondayFirst_AChangedRosterWinningOverTheSeededOne()
    {
        _fixture.StoredRosters.Add(new Roster(Anouk, new Dictionary<DayOfWeek, ShiftTimes>
        {
            [DayOfWeek.Sunday] = new(new TimeOnly(10, 0), new TimeOnly(18, 0)),
            [DayOfWeek.Tuesday] = new(new TimeOnly(5, 0), new TimeOnly(13, 0)),
        }));

        var rosters = await new ListRostersHandler(_fixture.Rosters, _fixture.Drivers).Handle(new ListRostersQuery(), CancellationToken.None);

        rosters.Select(roster => roster.DriverId).Should().Equal("DRV-0001", "DRV-0002");
        rosters[0].Weekdays.Select(weekday => weekday.Weekday).Should().Equal(DayOfWeek.Tuesday, DayOfWeek.Sunday);
        rosters[1].Weekdays.Should().HaveCount(5);
    }

    [Fact]
    public async Task ListRosters_ADriverWithoutARoster_HasNoWeekdays()
    {
        _fixture.SeededRosters.List().Returns([]);

        var rosters = await new ListRostersHandler(_fixture.Rosters, _fixture.Drivers).Handle(new ListRostersQuery(), CancellationToken.None);

        rosters.Should().OnlyContain(roster => roster.Weekdays.Count == 0);
    }
}
