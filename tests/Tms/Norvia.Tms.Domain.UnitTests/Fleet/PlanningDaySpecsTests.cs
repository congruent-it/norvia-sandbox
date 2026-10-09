using Ardalis.Specification;
using Norvia.Tms.Domain.Fleet;

namespace Norvia.Tms.Domain.UnitTests.Fleet;

public sealed class PlanningDaySpecsTests
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    [Fact]
    public void PlanningDaysFrom_ADate_ListsTheDaysFromThenOnInOrder()
    {
        var days = new[] { Day(Monday.AddDays(2)), Day(Monday.AddDays(-1)), Day(Monday) };

        new PlanningDaysFromSpec(Monday).Evaluate(days).Select(day => day.Date).Should().Equal(Monday, Monday.AddDays(2));
    }

    [Fact]
    public void OpenPlanningDays_SkipsClosedDays_AndListsTheRestInOrder()
    {
        var closed = Day(Monday.AddDays(-1));
        closed.Close();
        var days = new[] { Day(Monday.AddDays(1)), closed, Day(Monday) };

        new OpenPlanningDaysSpec().Evaluate(days).Select(day => day.Date).Should().Equal(Monday, Monday.AddDays(1));
    }

    [Fact]
    public void PlanningDayByDate_FindsThatDay()
    {
        var days = new[] { Day(Monday), Day(Monday.AddDays(1)) };

        new PlanningDayByDateSpec(Monday.AddDays(1)).Evaluate(days).Should().ContainSingle().Which.Date.Should().Be(Monday.AddDays(1));
    }

    private static PlanningDay Day(DateOnly date) => PlanningDay.Open(date, [], []);
}
