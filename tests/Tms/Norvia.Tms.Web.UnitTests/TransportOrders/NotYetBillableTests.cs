using AngleSharp.Dom;
using Norvia.Tms.Application.Billing.ListNotYetBillable;
using Norvia.Tms.Domain.TransportOrders;
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

    [Fact]
    public void Render_OffersOnlyTheActionsThatCanLiftABlockingReason()
    {
        Queries.ListNotYetBillableAsync(Arg.Any<CancellationToken>()).Returns([
            Order("TO-0000043", "SH-0000012", BillingBlockingReason.NotRated, BillingBlockingReason.NoProofOfDelivery),
            Order("TO-0000044", "SH-0000013", BillingBlockingReason.OpenException),
            Order("TO-0000045", "SH-0000014", BillingBlockingReason.NotRated)]);

        var page = Render<NotYetBillable>();

        page.WaitForElement("tbody tr");
        ActionsOf(page).Should().Equal(
            "Open the rating exception of TO-0000043", "Release TO-0000043 without Proof of delivery",
            "Open the rating exception of TO-0000045");
        page.Find("a[aria-label='Open the rating exception of TO-0000043']").GetAttribute("href").Should().Be("/transport-orders/TO-0000043");
    }

    [Fact]
    public void Release_AsksForARemark_AndSendsItAsTheCustomerServiceRemark()
    {
        var page = RenderWithOneToRelease();
        Commands.ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post", Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>())
            .Returns(new ReleaseWithoutProofOfDeliveryAnswer.Released(BillingState.Billable));

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery'] fluent-text-area").Input("  CMR by post ");
        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Commands.Received(1).ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post", Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("TO-0000043 is released without a Proof of delivery and is Billable now."));
        page.FindAll("section[aria-label^='Release']").Should().BeEmpty();
    }

    [Fact]
    public void Release_ThatLeavesAnotherReason_SaysTheOrderIsNotBillableYet()
    {
        var page = RenderWithOneToRelease();
        Commands.ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post", Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>())
            .Returns(new ReleaseWithoutProofOfDeliveryAnswer.Released(BillingState.NotYetBillable));

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery'] fluent-text-area").Input("CMR by post");
        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("another reason still keeps it from being Billable"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Release_WithoutARemark_IsNotSent(string remark)
    {
        var page = RenderWithOneToRelease();

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery'] fluent-text-area").Input(remark);
        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).QuerySelector(".validation-message")!.TextContent.Should().Contain("Give a remark"));
        Commands.DidNotReceive().ReleaseWithoutProofOfDeliveryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Release_WithATooLongRemark_IsNotSent()
    {
        var page = RenderWithOneToRelease();

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery'] fluent-text-area").Input(new string('x', TransportOrder.MaxReleaseRemarkLength + 1));
        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => Panel(page).QuerySelector(".validation-message")!.TextContent.Should().Contain($"at most {TransportOrder.MaxReleaseRemarkLength} characters"));
        Commands.DidNotReceive().ReleaseWithoutProofOfDeliveryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Release_TheTmsRefuses_KeepsTheFormOpen_WithTheReason()
    {
        var page = RenderWithOneToRelease();
        Commands.ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post", Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>())
            .Returns(new ReleaseWithoutProofOfDeliveryAnswer.Refused("It was released already."));

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery'] fluent-text-area").Input("CMR by post");
        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Transport Order TO-0000043 wasn't released: It was released already."));
        Panel(page);
    }

    [Fact]
    public void Release_WhenTheTmsCantBeReached_KeepsTheFormOpen_AndSaysToTryAgain()
    {
        var page = RenderWithOneToRelease();
        Commands.ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post", Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>())
            .Returns<ReleaseWithoutProofOfDeliveryAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery'] fluent-text-area").Input("CMR by post");
        Panel(page).QuerySelector("form")!.Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("couldn't be released. Try again."));
        Panel(page);
    }

    [Fact]
    public void Release_CanBeClosedWithoutReleasing()
    {
        var page = RenderWithOneToRelease();

        page.Find("fluent-button[aria-label='Release TO-0000043 without Proof of delivery']").Click();
        Click(Panel(page), "Cancel");

        page.WaitForAssertion(() => page.FindAll("section[aria-label^='Release']").Should().BeEmpty());
        Commands.DidNotReceive().ReleaseWithoutProofOfDeliveryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Norvia.Agents.AgentRun?>(), Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<NotYetBillable> RenderWithOneToRelease()
    {
        Queries.ListNotYetBillableAsync(Arg.Any<CancellationToken>()).Returns([Order("TO-0000043", "SH-0000012", BillingBlockingReason.NoProofOfDelivery)]);
        var page = Render<NotYetBillable>();
        page.WaitForElement("tbody tr");
        return page;
    }

    private static IElement Panel(IRenderedComponent<NotYetBillable> page) =>
        page.WaitForElement("section[aria-label='Release TO-0000043 without Proof of delivery']");

    private static void Click(IElement panel, string text) =>
        panel.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == text).Click();

    private static List<string> ActionsOf(IRenderedComponent<NotYetBillable> page) =>
        [.. page.FindAll("tbody fluent-button, tbody a[aria-label]").Select(action => action.GetAttribute("aria-label")!)];
}
