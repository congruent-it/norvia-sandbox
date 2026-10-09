using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class DrivingHourBudgetTests
{
    private static readonly DriverId Driver = new("DRV-0001");
    private static readonly DrivingHourLimits Limits = DrivingHourLimits.Default;

    // Wednesday 14 October 2026: its fixed week is Monday 12 to Sunday 18 October, the previous one 5 to 11 October.
    private static readonly DateOnly Wednesday = new(2026, 10, 14);

    private static PlannedDriving Drive(int month, int day, double hours) => new(Driver, new DateOnly(2026, month, day), TimeSpan.FromHours(hours));

    private static DrivingHoursLeft Left(params PlannedDriving[] trips) => DrivingHourBudget.Left(trips, Wednesday, Limits);

    [Fact]
    public void Left_WithNoTrips_IsEveryBudgetFull()
    {
        Left().Should().Be(new DrivingHoursLeft(TimeSpan.FromHours(9), TimeSpan.FromHours(56), TimeSpan.FromHours(90), 2));
    }

    [Fact]
    public void Left_TripsToday_UseUpTheDay()
    {
        var left = Left(Drive(10, 14, 3), Drive(10, 14, 2.5));

        left.Today.Should().Be(TimeSpan.FromHours(3.5));
        left.ExtensionsLeft.Should().Be(2);
    }

    [Fact]
    public void Left_ADayOverNineHours_UsesAnExtensionAndIsMeasuredAgainstTen()
    {
        var left = Left(Drive(10, 14, 9.5));

        left.Today.Should().Be(TimeSpan.FromHours(0.5));
        left.ExtensionsLeft.Should().Be(1);
    }

    [Fact]
    public void Left_ExactlyNineHours_UsesNoExtension()
    {
        var left = Left(Drive(10, 14, 9));

        left.Today.Should().Be(TimeSpan.Zero);
        left.ExtensionsLeft.Should().Be(2);
    }

    [Fact]
    public void Left_TwoExtendedDaysInTheWeek_LeaveNoExtension()
    {
        var left = Left(Drive(10, 12, 10), Drive(10, 13, 9.5));

        left.ExtensionsLeft.Should().Be(0);
    }

    [Fact]
    public void Left_ExtendedDaysLaterInTheWeek_AlsoUseExtensions()
    {
        var left = Left(Drive(10, 16, 10), Drive(10, 18, 10));

        left.ExtensionsLeft.Should().Be(0);
    }

    [Fact]
    public void Left_ExtendedDaysInThePreviousWeek_DoNotUseThisWeeksExtensions()
    {
        var left = Left(Drive(10, 5, 10), Drive(10, 6, 10));

        left.ExtensionsLeft.Should().Be(2);
    }

    [Fact]
    public void Left_TheWeekIsFixedFromMondayToSunday_SoSundayBelongsToThePreviousWeek()
    {
        // Sunday 11 October is last week's: it counts toward the fortnight but not this week.
        var left = Left(Drive(10, 11, 8), Drive(10, 12, 8));

        left.Week.Should().Be(TimeSpan.FromHours(48));
        left.Fortnight.Should().Be(TimeSpan.FromHours(74));
    }

    [Fact]
    public void Left_TheWeekIncludesDaysAfterTodayThatAreAlreadyPlanned()
    {
        var left = Left(Drive(10, 14, 8), Drive(10, 18, 8));

        left.Week.Should().Be(TimeSpan.FromHours(40));
    }

    [Fact]
    public void Left_TheFortnightIsThisWeekAndThePreviousOne()
    {
        // Monday 5 October is in the fortnight, Sunday 4 October is the week before and no longer counts.
        var left = Left(Drive(10, 4, 9), Drive(10, 5, 9));

        left.Fortnight.Should().Be(TimeSpan.FromHours(81));
    }

    [Fact]
    public void Left_OnMonday_TheWeekStartsAgainWhileThePreviousWeekStillCountsInTheFortnight()
    {
        var monday = new DateOnly(2026, 10, 12);
        var trips = new[] { Drive(10, 9, 9), Drive(10, 10, 9), Drive(10, 12, 4) };

        var left = DrivingHourBudget.Left(trips, monday, Limits);

        left.Today.Should().Be(TimeSpan.FromHours(5));
        left.Week.Should().Be(TimeSpan.FromHours(52));
        left.Fortnight.Should().Be(TimeSpan.FromHours(68));
    }

    [Fact]
    public void Left_MoreThanTheLimit_NeverGoesBelowZero()
    {
        var left = Left(Drive(10, 12, 10), Drive(10, 13, 10), Drive(10, 14, 10), Drive(10, 15, 10), Drive(10, 16, 10), Drive(10, 17, 10));

        left.Week.Should().Be(TimeSpan.Zero);
        left.Today.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Left_UsesTheGivenLimits()
    {
        var tight = new DrivingHourLimits(TimeSpan.FromHours(4), TimeSpan.FromHours(5), 1, TimeSpan.FromHours(10), TimeSpan.FromHours(15));

        var left = DrivingHourBudget.Left([Drive(10, 14, 4.5)], Wednesday, tight);

        left.Should().Be(new DrivingHoursLeft(TimeSpan.FromHours(0.5), TimeSpan.FromHours(5.5), TimeSpan.FromHours(10.5), 0));
    }

    [Fact]
    public void Left_ADayOverNineHours_WithBothExtensionsUsedElsewhere_HasNothingLeft()
    {
        var left = Left(Drive(10, 12, 10), Drive(10, 13, 10), Drive(10, 14, 9.5));

        left.Today.Should().Be(TimeSpan.Zero);
        left.ExtensionsLeft.Should().Be(0);
    }

    [Fact]
    public void Window_IsFromTheMondayOfThePreviousWeekToTheSundayOfThisOne()
    {
        DrivingHourBudget.Window(Wednesday).Should().Be((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18)));
        DrivingHourBudget.Window(new DateOnly(2026, 10, 18)).Should().Be((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18)));
    }

    [Fact]
    public void Left_WhenItIsMondayInAmsterdamButStillSundayInUtc_TheNewWeekHasStarted()
    {
        // 00:30 in Amsterdam on Monday 12 October (CEST) is 22:30 UTC on Sunday the 11th.
        var instant = new DateTimeOffset(2026, 10, 11, 22, 30, 0, TimeSpan.Zero);
        var today = new PlanningHorizonRules(14, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")).Today(instant);

        var left = DrivingHourBudget.Left([Drive(10, 11, 8), Drive(10, 12, 2)], today, Limits);

        left.Week.Should().Be(TimeSpan.FromHours(54));
        left.Fortnight.Should().Be(TimeSpan.FromHours(80));
        left.Today.Should().Be(TimeSpan.FromHours(7));
    }
}
