using Ardalis.Specification;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class AvailabilityRecordSpecsTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DriverId Anouk = new("DRV-0001");
    private static readonly DriverId Joost = new("DRV-0002");
    private static readonly VehicleId Truck = new("VEH-0001");
    private static readonly VehicleId Van = new("VEH-0002");

    [Fact]
    public void AbsencesFrom_ListsTheAbsencesFromThatDateInOrder()
    {
        var absences = new[] { Absence.Record(Anouk, Monday.AddDays(3), AbsenceReason.Leave, null), Absence.Record(Anouk, Monday.AddDays(-1), AbsenceReason.Leave, null), Absence.Record(Joost, Monday, AbsenceReason.Training, null) };

        new AbsencesFromSpec(Monday).Evaluate(absences).Select(absence => absence.Date).Should().Equal(Monday, Monday.AddDays(3));
    }

    [Fact]
    public void AbsencesOfDriverBetween_ListsOnlyThatDriversAbsencesInThePeriod()
    {
        var absences = new[]
        {
            Absence.Record(Anouk, Monday, AbsenceReason.Leave, null),
            Absence.Record(Anouk, Monday.AddDays(5), AbsenceReason.Leave, null),
            Absence.Record(Joost, Monday, AbsenceReason.Leave, null),
        };

        new AbsencesOfDriverBetweenSpec(Anouk, Monday, Monday.AddDays(2)).Evaluate(absences).Should().ContainSingle().Which.Should().BeSameAs(absences[0]);
    }

    [Fact]
    public void AbsenceById_FindsThatAbsence()
    {
        var absences = new[] { Absence.Record(Anouk, Monday, AbsenceReason.Leave, null), Absence.Record(Joost, Monday, AbsenceReason.Leave, null) };

        new AbsenceByIdSpec(absences[1].Id).Evaluate(absences).Should().ContainSingle().Which.Should().BeSameAs(absences[1]);
    }

    [Fact]
    public void OffTheRoadDaysFrom_ListsTheDaysFromThatDateInOrder()
    {
        var days = new[] { OffTheRoadDay.Record(Truck, Monday.AddDays(2), OffTheRoadReason.Repair, null), OffTheRoadDay.Record(Truck, Monday.AddDays(-2), OffTheRoadReason.Repair, null) };

        new OffTheRoadDaysFromSpec(Monday).Evaluate(days).Should().ContainSingle().Which.Date.Should().Be(Monday.AddDays(2));
    }

    [Fact]
    public void OffTheRoadDaysOfVehicleBetween_ListsOnlyThatVehiclesDaysInThePeriod()
    {
        var days = new[]
        {
            OffTheRoadDay.Record(Truck, Monday, OffTheRoadReason.Repair, null),
            OffTheRoadDay.Record(Van, Monday, OffTheRoadReason.Repair, null),
            OffTheRoadDay.Record(Truck, Monday.AddDays(9), OffTheRoadReason.Repair, null),
        };

        new OffTheRoadDaysOfVehicleBetweenSpec(Truck, Monday, Monday.AddDays(1)).Evaluate(days).Should().ContainSingle().Which.Should().BeSameAs(days[0]);
    }

    [Fact]
    public void OffTheRoadDayById_FindsThatDay()
    {
        var days = new[] { OffTheRoadDay.Record(Truck, Monday, OffTheRoadReason.Repair, null), OffTheRoadDay.Record(Van, Monday, OffTheRoadReason.Repair, null) };

        new OffTheRoadDayByIdSpec(days[0].Id).Evaluate(days).Should().ContainSingle().Which.Should().BeSameAs(days[0]);
    }

    [Fact]
    public void OpenPlanningDaysFrom_ListsOnlyOpenDaysFromThatDate()
    {
        var closed = PlanningDay.Open(Monday.AddDays(1), [], []);
        closed.Close();
        var days = new[] { PlanningDay.Open(Monday.AddDays(-1), [], []), PlanningDay.Open(Monday, [], []), closed };

        new OpenPlanningDaysFromSpec(Monday).Evaluate(days).Should().ContainSingle().Which.Date.Should().Be(Monday);
    }

    [Fact]
    public void RosterByDriver_FindsThatDriversRoster()
    {
        var rosters = new[] { new Roster(Anouk, new Dictionary<DayOfWeek, ShiftTimes>()), new Roster(Joost, new Dictionary<DayOfWeek, ShiftTimes>()) };

        new RosterByDriverSpec(Joost).Evaluate(rosters).Should().ContainSingle().Which.DriverId.Should().Be(Joost);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 91, true)]
    [InlineData(0, 92, false)]
    [InlineData(1, 0, false)]
    public void AvailabilityPeriod_IsValidWhenItEndsOnOrAfterItStartsAndIsntTooLong(int fromOffset, int toOffset, bool valid)
    {
        AvailabilityPeriod.IsValid(Monday.AddDays(fromOffset), Monday.AddDays(toOffset)).Should().Be(valid);
    }

    [Fact]
    public void AvailabilityPeriod_Days_CoversFirstToLastInclusive()
    {
        AvailabilityPeriod.Days(Monday, Monday.AddDays(2)).Should().Equal(Monday, Monday.AddDays(1), Monday.AddDays(2));
    }

    [Fact]
    public void Refusals_NameTheirStableCodeAndAReadableMessage()
    {
        PlanningRefusal.VehicleNotFound("VEH-9").Should().Match<PlanningRefusal>(refusal => refusal.Code == PlanningRefusalCode.VehicleNotFound && refusal.Message.Contains("VEH-9", StringComparison.Ordinal));
        PlanningRefusal.AbsenceNotFound().Code.Should().Be(PlanningRefusalCode.AbsenceNotFound);
        PlanningRefusal.OffTheRoadDayNotFound().Code.Should().Be(PlanningRefusalCode.OffTheRoadDayNotFound);
        PlanningRefusal.PeriodInvalid(92).Message.Should().Contain("92");
        PlanningRefusal.RemarkTooLong(500).Message.Should().Contain("500");
    }
}
