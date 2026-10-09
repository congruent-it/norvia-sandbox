using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class PlanningDayTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);
    private static readonly DriverId Anouk = new("DRV-0001");
    private static readonly DriverId Joost = new("DRV-0002");

    [Fact]
    public void Open_ADriverWhoseRosterWorksThatWeekday_GetsTheRostersShift()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, DayOfWeek.Monday, 6, 14)], [new VehicleId("VEH-0001")]);

        day.Status.Should().Be(PlanningDayStatus.Open);
        day.Date.Should().Be(Monday);
        day.Shifts.Should().ContainSingle().Which.Should().Be(new Shift(Anouk, new TimeOnly(6, 0), new TimeOnly(14, 0), EditedByHand: false));
        day.AvailableVehicles.Should().Equal(new VehicleId("VEH-0001"));
    }

    [Fact]
    public void Open_ADriverWhoseRosterHasNoWorkThatWeekday_GetsNoShift()
    {
        var saturday = Monday.AddDays(5);

        var day = PlanningDay.Open(saturday, [Weekdays(Anouk, DayOfWeek.Monday, 6, 14)], []);

        day.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void EditShift_OnAnOpenDay_ReplacesTheShiftAndMarksItEditedByHand()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, DayOfWeek.Monday, 6, 14), Weekdays(Joost, DayOfWeek.Monday, 8, 16)], []);

        var refusal = day.EditShift(Anouk, new TimeOnly(5, 0), new TimeOnly(13, 0));

        refusal.Should().BeNull();
        day.Shifts.Should().BeEquivalentTo(
            [new Shift(Joost, new TimeOnly(8, 0), new TimeOnly(16, 0), false), new Shift(Anouk, new TimeOnly(5, 0), new TimeOnly(13, 0), true)]);
    }

    [Fact]
    public void EditShift_ForADriverWithoutAShift_AddsOne()
    {
        var day = PlanningDay.Open(Monday, [], []);

        day.EditShift(Anouk, new TimeOnly(9, 0), new TimeOnly(17, 0));

        day.Shifts.Should().ContainSingle().Which.EditedByHand.Should().BeTrue();
    }

    [Fact]
    public void EditShift_OnAClosedDay_IsRefusedAndChangesNothing()
    {
        var day = PlanningDay.Open(Monday, [Weekdays(Anouk, DayOfWeek.Monday, 6, 14)], []);
        day.Close();

        var refusal = day.EditShift(Anouk, new TimeOnly(5, 0), new TimeOnly(13, 0));

        refusal.Should().NotBeNull();
        refusal!.Code.Should().Be(PlanningRefusalCode.DayClosed);
        day.Shifts.Should().ContainSingle().Which.Start.Should().Be(new TimeOnly(6, 0));
    }

    [Fact]
    public void EditShift_EndingBeforeItStarts_IsRefused()
    {
        var day = PlanningDay.Open(Monday, [], []);

        var refusal = day.EditShift(Anouk, new TimeOnly(14, 0), new TimeOnly(6, 0));

        refusal!.Code.Should().Be(PlanningRefusalCode.ShiftInvalid);
        day.Shifts.Should().BeEmpty();
    }

    [Fact]
    public void Close_AClosedDay_StaysClosed()
    {
        var day = PlanningDay.Open(Monday, [], []);

        day.Close();
        day.Close();

        day.Status.Should().Be(PlanningDayStatus.Closed);
    }

    private static Roster Weekdays(DriverId driver, DayOfWeek weekday, int startHour, int endHour) =>
        new(driver, new Dictionary<DayOfWeek, ShiftTimes> { [weekday] = new(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)) });
}
