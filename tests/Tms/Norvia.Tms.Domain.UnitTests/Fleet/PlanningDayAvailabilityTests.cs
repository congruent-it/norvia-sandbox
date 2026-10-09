using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class PlanningDayAvailabilityTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DriverId Anouk = new("DRV-0001");
    private static readonly DriverId Joost = new("DRV-0002");
    private static readonly VehicleId Truck = new("VEH-0001");
    private static readonly VehicleId Van = new("VEH-0002");

    [Fact]
    public void Open_ADriverWithAnAbsence_GetsNoShift()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14), Weekdays(Joost, 8, 16)], [], absentDrivers: new HashSet<DriverId> { Anouk });

        day.Shifts.Should().ContainSingle().Which.DriverId.Should().Be(Joost);
    }

    [Fact]
    public void Open_AVehicleOffTheRoad_IsNotAvailable()
    {
        var day = PlanningDay.Open(Monday, [], [Truck, Van], offTheRoadVehicles: new HashSet<VehicleId> { Truck });

        day.AvailableVehicles.Should().Equal(Van);
    }

    [Fact]
    public void RecordAbsence_OnAnOpenDay_RemovesTheShiftEvenWhenEditedByHand()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14)], []);
        day.EditShift(Anouk, new TimeOnly(5, 0), new TimeOnly(13, 0));

        day.RecordAbsence(Anouk).Should().BeNull();

        day.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void RecordAbsence_OnAClosedDay_IsRefusedWithDayClosed()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14)], []);
        day.Close();

        day.RecordAbsence(Anouk)!.Code.Should().Be(PlanningRefusalCode.DayClosed);
        day.Shifts.Should().ContainSingle();
    }

    [Fact]
    public void EndAbsence_GivesTheShiftFromTheRosterBack()
    {
        var roster = Weekdays(Anouk, 6, 14);
        var day = PlanningDay.Open(Monday, [roster], [], new HashSet<DriverId> { Anouk });

        day.EndAbsence(Anouk, roster).Should().BeNull();

        day.Shifts.Should().ContainSingle().Which.Should().Be(new Shift(Anouk, new TimeOnly(6, 0), new TimeOnly(14, 0), false));
    }

    [Fact]
    public void EndAbsence_ForADriverWithoutARoster_LeavesNoShift()
    {
        var day = PlanningDay.Open(Monday, [], []);

        day.EndAbsence(Anouk, null);

        day.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void EndAbsence_OnAClosedDay_IsRefused()
    {
        var day = PlanningDay.Open(Monday, [], []);
        day.Close();

        day.EndAbsence(Anouk, null)!.Code.Should().Be(PlanningRefusalCode.DayClosed);
    }

    [Fact]
    public void TakeOffTheRoad_RemovesTheVehicleAndPutBackOnTheRoadReturnsIt()
    {
        var day = PlanningDay.Open(Monday, [], [Truck, Van]);

        day.TakeOffTheRoad(Truck).Should().BeNull();
        day.AvailableVehicles.Should().Equal(Van);

        day.PutBackOnTheRoad(Truck).Should().BeNull();
        day.PutBackOnTheRoad(Truck);
        day.AvailableVehicles.Should().BeEquivalentTo([Van, Truck]);
    }

    [Fact]
    public void TakeOffTheRoadAndPutBack_OnAClosedDay_AreRefused()
    {
        var day = PlanningDay.Open(Monday, [], [Truck]);
        day.Close();

        day.TakeOffTheRoad(Truck)!.Code.Should().Be(PlanningRefusalCode.DayClosed);
        day.PutBackOnTheRoad(Van)!.Code.Should().Be(PlanningRefusalCode.DayClosed);
        day.AvailableVehicles.Should().Equal(Truck);
    }

    [Fact]
    public void ApplyRoster_OnADayNotEditedByHandWithoutAnAbsence_RewritesTheShift()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14)], []);

        day.ApplyRoster(Weekdays(Anouk, 7, 15), absent: false).Should().BeTrue();

        day.Shifts.Should().ContainSingle().Which.Should().Be(new Shift(Anouk, new TimeOnly(7, 0), new TimeOnly(15, 0), false));
    }

    [Fact]
    public void ApplyRoster_WhenTheNewRosterHasNoWorkThatWeekday_RemovesTheShift()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14)], []);

        day.ApplyRoster(new Roster(Anouk, new Dictionary<DayOfWeek, ShiftTimes>()), absent: false);

        day.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRoster_OnADayWithAShiftEditedByHand_KeepsTheEdit()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14)], []);
        day.EditShift(Anouk, new TimeOnly(5, 0), new TimeOnly(13, 0));

        day.ApplyRoster(Weekdays(Anouk, 7, 15), absent: false).Should().BeFalse();

        day.Shifts.Should().ContainSingle().Which.Should().Be(new Shift(Anouk, new TimeOnly(5, 0), new TimeOnly(13, 0), true));
    }

    [Fact]
    public void ApplyRoster_ForADriverWithAnAbsence_AddsNoShift()
    {
        var day = PlanningDay.Open(Monday, [], []);

        day.ApplyRoster(Weekdays(Anouk, 7, 15), absent: true).Should().BeFalse();

        day.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRoster_OnAClosedDay_ChangesNothing()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, 6, 14)], []);
        day.Close();

        day.ApplyRoster(Weekdays(Anouk, 7, 15), absent: false).Should().BeFalse();

        day.Shifts.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(6, 0));
    }

    [Fact]
    public void ApplyRoster_ForADriverWithoutAShiftThatDay_LeavesOtherDriversAlone()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Joost, 8, 16)], []);

        day.ApplyRoster(Weekdays(Anouk, 7, 15), absent: false);

        day.Shifts.Should().HaveCount(2);
    }

    private static Roster Weekdays(DriverId driver, int startHour, int endHour) =>
        new(driver, new Dictionary<DayOfWeek, ShiftTimes> { [DayOfWeek.Monday] = new(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)) });
}
