using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class NotYetBillableTests : TmsWebContext
{
    private static TransportOrderNotYetBillableSummary Order(string number, string shipment, params BillingBlockingReason[] reasons) => new(
        number, shipment, Samples.BrouwerDranken, "BD-2026-0043", new DateTimeOffset(2026, 9, 24, 9, 31, 0, TimeSpan.Zero), reasons);

    [Fact]
    public void Render_BeforeTheListIsRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<TransportOrderNotYetBillableSummary>>();
        Queries.ListNotYetBillableAsync(Arg.Any<CancellationToken>())
            .Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<NotYetBillable>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Transport Orders not yet billable");
    }

    [Fact]
    public void Render_WithNoOrders_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListNotYetBillableAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<NotYetBillable>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("No delivered Transport Order is waiting to become billable."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithOrders_ListsEachWithShipperReferenceDeliveryTimeAndReasonsInTheOrderGiven()
    {
        Queries.ListNotYetBillableAsync(Arg.Any<CancellationToken>()).Returns([
            Order("TO-0000043", "SH-0000012", BillingBlockingReason.NotRated, BillingBlockingReason.NoProofOfDelivery),
            Order("TO-0000044", "SH-0000013", BillingBlockingReason.OpenException)]);

        var page = Render<NotYetBillable>();

        var row = page.WaitForElement("tr:has(a[href='/transport-orders/TO-0000043'])");
        row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).Take(5).Should().Equal(
            "TO-0000043", "Brouwer Dranken", "BD-2026-0043", "Thu 24 Sep 2026, 11:31", "Not rated, No Proof of delivery");
        row.QuerySelector("a[href='/shipments/SH-0000012']").Should().NotBeNull();
        page.Find("tr:has(a[href='/transport-orders/TO-0000044'])").TextContent.Should().Contain("Open Exception");
    }

    [Fact]
    public void Render_WhenAnOrderBecomesBillableWhileTheListIsOpen_DropsItWithoutARefresh()
    {
        var first = Order("TO-0000043", "SH-0000012", BillingBlockingReason.NoProofOfDelivery);
        var second = Order("TO-0000044", "SH-0000013", BillingBlockingReason.NoProofOfDelivery);
        Queries.ListNotYetBillableAsync(Arg.Any<CancellationToken>()).Returns([first, second], [second]);
        var page = Render<NotYetBillable>();
        page.WaitForElement("a[href='/transport-orders/TO-0000043']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/transport-orders/TO-0000043']").Should().BeEmpty());
        page.Find("a[href='/transport-orders/TO-0000044']");
    }
}
