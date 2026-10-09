using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class PlanningHorizonRulesTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private readonly PlanningHorizonRules _rules = new(14, Amsterdam);

    [Fact]
    public void Today_JustAfterMidnightInAmsterdam_IsTheNewDay_WhileUtcIsStillYesterday()
    {
        // 00:05 in Amsterdam on 12 October (CEST, UTC+2) is 22:05 UTC on the 11th.
        var now = new DateTimeOffset(2026, 10, 11, 22, 5, 0, TimeSpan.Zero);

        _rules.Today(now).Should().Be(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public void Today_JustBeforeMidnightInAmsterdam_IsStillTheOldDay()
    {
        var now = new DateTimeOffset(2026, 10, 11, 21, 55, 0, TimeSpan.Zero);

        _rules.Today(now).Should().Be(new DateOnly(2026, 10, 11));
    }

    [Fact]
    public void DaysFrom_Today_IsTodayAndTheNextFourteen()
    {
        var days = _rules.DaysFrom(new DateOnly(2026, 10, 12)).ToList();

        days.Should().HaveCount(15);
        days[0].Should().Be(new DateOnly(2026, 10, 12));
        days[^1].Should().Be(new DateOnly(2026, 10, 26));
    }

    [Fact]
    public void DaysFrom_AShorterHorizon_IsTodayAndThoseDays()
    {
        new PlanningHorizonRules(3, Amsterdam).DaysFrom(new DateOnly(2026, 10, 12)).Should().HaveCount(4);
    }
}
