using Norvia.Tms.Web.Planner;
using Norvia.Tms.Web.Roles;

namespace Norvia.Tms.Web.UnitTests.Planner;

public sealed class PlannerHomeItemTests
{
    [Fact]
    public void From_ListsTheItemsInTheOrderThePlannerWorksWithTheirCountsAndLists()
    {
        var items = PlannerHomeItem.From(new PlannerHomeCounts(1, 2, 3, 4, 5, 6));

        items.Select(item => (item.Title, item.Path, item.Count)).Should().Equal(
            ("Waiting for planning", "/transport-orders", 1),
            ("Shipments to release", "/shipments/to-release", 2),
            ("Bookings awaiting an answer", "/shipments/awaiting-answer", 3),
            ("Open Exceptions", "/exceptions", 4),
            ("Delivered without Proof of delivery", "/shipments/delivered-without-proof-of-delivery", 5),
            ("Shipments to put on Trips", "/trips/shipments", 6));
    }

    [Fact]
    public void From_EveryItemLinksToAViewOfThePlanner()
    {
        var items = PlannerHomeItem.From(new PlannerHomeCounts(0, 0, 0, 0, 0, 0));

        items.Select(item => item.Path).Should().BeSubsetOf(NorviaRole.Planner.Views.Select(view => view.Path));
    }

    [Fact]
    public void From_EveryItemNamesTheActionToTake()
    {
        var items = PlannerHomeItem.From(new PlannerHomeCounts(1, 1, 1, 1, 1, 1));

        items.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.Action));
    }
}
