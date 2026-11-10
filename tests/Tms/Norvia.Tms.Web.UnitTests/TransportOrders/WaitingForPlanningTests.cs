using AngleSharp.Dom;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders.ToPlan;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class WaitingForPlanningTests : TmsWebContext
{
    public WaitingForPlanningTests()
    {
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns([Samples.Kessler, Samples.VanDijk]);
    }

    [Fact]
    public void Render_BeforeTheTransportOrdersAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<TransportOrderToPlanSummary>>();
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>())
            .Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<WaitingForPlanning>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Transport Orders waiting for planning");
    }

    [Fact]
    public void Render_WithTransportOrdersWaiting_ListsEachWithShipperPlacesCollectionWindowInRotterdamTimeAndRatingResult()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([Samples.ToPlan("TO-0000042", "BD-2026-0042")]);

        var page = Render<WaitingForPlanning>();

        var row = page.WaitForElement("tr:has(a[href='/transport-orders/TO-0000042'])");
        var cells = row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim());
        cells.Should().Equal(
            string.Empty, // The checkbox that selects it.
            "TO-0000042",
            "Brouwer Dranken",
            "BD-2026-0042",
            "Rotterdam, NL",
            "Utrecht, NL",
            "Sat 26 Sep 2026, 06:00–10:00",
            "Not yet rated",
            string.Empty); // The Suggest button, which shows only with Agents:Enabled on.
    }

    [Fact]
    public void Render_WhenThePageCouldNotReadTheRole_OffersNoSuggest_EvenWithAgentsOn()
    {
        // The role comes from the request's cookie; the Planner view's Suggest button itself is covered by SuggestAssignmentTests and the UI tests.
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([Samples.ToPlan("TO-0000042")]);
        AgentSettings.Enabled = true;

        var page = Render<WaitingForPlanning>();

        page.WaitForElement("tr:has(a[href='/transport-orders/TO-0000042'])");
        page.FindAll("[data-explain=suggest-assignment]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WithTransportOrdersWaiting_ListsThemInTheOrderTheQueryGives()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([Samples.ToPlan("TO-0000044"), Samples.ToPlan("TO-0000042")]);

        var page = Render<WaitingForPlanning>();

        page.WaitForAssertion(() => page.FindAll("a[href^='/transport-orders/']").Select(link => link.TextContent)
            .Should().Equal("TO-0000044", "TO-0000042"));
    }

    [Fact]
    public void Render_WithNoTransportOrdersWaiting_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<WaitingForPlanning>();

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("No Transport Orders are waiting for planning."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenATransportOrderIsAcceptedWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            [Samples.ToPlan("TO-0000042")],
            [Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043", "BD-2026-0043")]);
        var page = Render<WaitingForPlanning>();
        page.WaitForElement("a[href='/transport-orders/TO-0000042']");

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/transport-orders/TO-0000043']");
    }

    [Fact]
    public void Render_WhenATransportOrderIsRatedWhileTheListIsOpen_ShowsItsNewRatingResultWithoutARefresh()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            [Samples.ToPlan("TO-0000042", ratingResult: RatingResult.NotYetRated)],
            [Samples.ToPlan("TO-0000042", ratingResult: RatingResult.Rated)]);
        var page = Render<WaitingForPlanning>();
        page.WaitForAssertion(() => RatingResultOf(page, "TO-0000042").Should().Be("Not yet rated"));

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => RatingResultOf(page, "TO-0000042").Should().Be("Rated"));
    }

    [Fact]
    public void Render_ATransportOrderWhoseRatingFailed_StillListsItAsWaitingForPlanning()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>())
            .Returns([Samples.ToPlan("TO-0000042", ratingResult: RatingResult.RatingFailed)]);

        var page = Render<WaitingForPlanning>();

        page.WaitForAssertion(() => RatingResultOf(page, "TO-0000042").Should().Be("Rating failed"));
    }

    [Fact]
    public void Render_WhenAPlannedTransportOrderIsNoLongerToPlan_DropsItWithoutARefresh()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            [Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043")],
            [Samples.ToPlan("TO-0000043")]);
        var page = Render<WaitingForPlanning>();
        page.WaitForElement("a[href='/transport-orders/TO-0000042']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/transport-orders/TO-0000042']").Should().BeEmpty());
        page.Find("a[href='/transport-orders/TO-0000043']");
    }

    [Fact]
    public void Render_WhenTheTransportOrdersCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<TransportOrderToPlanSummary>>([Samples.ToPlan("TO-0000042")]));

        var page = Render<WaitingForPlanning>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The Transport Orders waiting for planning couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/transport-orders/TO-0000042']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsTheTransportOrdersAlreadyShownAndWarnsTheyMayBeOutOfDate()
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<IReadOnlyList<TransportOrderToPlanSummary>>([Samples.ToPlan("TO-0000042")]),
            _ => throw new TimeoutException("The database didn't answer."));
        var page = Render<WaitingForPlanning>();
        page.WaitForElement("a[href='/transport-orders/TO-0000042']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The list couldn't be refreshed and may be out of date. Trying again…"));
        page.Find("a[href='/transport-orders/TO-0000042']");
    }

    [Fact]
    public void Picker_OffersOwnFleetAndOnlyTheActiveCarriers()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"));

        page.WaitForAssertion(() => page.FindAll("fluent-select fluent-option").Select(option => option.TextContent.Trim())
            .Should().Equal("Choose who carries them", "Own fleet", "Kessler Spedition", "Van Dijk Transport"));
    }

    [Fact]
    public void Picker_WhileTheCarriersAreRead_SaysSo()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<CarrierView>>();
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = RenderWith(Samples.ToPlan("TO-0000042"));

        page.WaitForAssertion(() => AssignGroup(page).QuerySelector("[role=status]")!.TextContent.Should().Contain("Loading the Carriers…"));
    }

    [Fact]
    public void Picker_WhenTheCarriersCantBeRead_OffersOwnFleetAndSaysWhy()
    {
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CarrierView>>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderWith(Samples.ToPlan("TO-0000042"));

        page.WaitForAssertion(() => AssignGroup(page).QuerySelector("[role=alert]")!.TextContent.Should()
            .Contain("The Carriers couldn't be loaded, so only Own fleet can be chosen."));
        page.FindAll("fluent-select fluent-option").Select(option => option.TextContent.Trim())
            .Should().Equal("Choose who carries them", "Own fleet");
    }

    [Fact]
    public void Assign_WithNothingSelected_CantBeSent()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"));

        Choose(page, Samples.Kessler.Id);

        AssignButton(page).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Assign_WithNoOneChosenToCarryThem_CantBeSent()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"));

        Select(page, "TO-0000042");

        AssignButton(page).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Assign_ToACarrier_AssignsEachSelectedOrderToItAndSaysWhichShipmentPlansIt()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043"), Samples.ToPlan("TO-0000044"));
        Commands.AssignAsync("TO-0000042", "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        Commands.AssignAsync("TO-0000044", "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000013"));
        Select(page, "TO-0000042");
        Select(page, "TO-0000044");
        Choose(page, Samples.Kessler.Id);

        AssignButton(page).TextContent.Trim().Should().Be("Assign selected (2)");
        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Transport Order TO-0000042 is planned as Shipment SH-0000012, carried by Kessler Spedition.",
            "Transport Order TO-0000044 is planned as Shipment SH-0000013, carried by Kessler Spedition."));
        page.Find("a[href='/shipments/SH-0000012']");
        Commands.DidNotReceive().AssignAsync("TO-0000043", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Assign_ToOwnFleet_AssignsWithoutACarrier()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"));
        Commands.AssignAsync("TO-0000042", null, Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        Select(page, "TO-0000042");
        Choose(page, "own-fleet");

        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Transport Order TO-0000042 is planned as Shipment SH-0000012, carried by Own fleet."));
    }

    [Fact]
    public void Assign_ToOwnFleetOnADayOverCapacity_ShowsTheLoadWarningBesideThePlannedMessage()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"));
        Commands.AssignAsync("TO-0000042", null, Arg.Any<CancellationToken>())
            .Returns(new AssignAnswer.Planned("SH-0000012", "Thu 1 Oct: Over capacity, 34 of 30 pallet places after this assignment"));
        Select(page, "TO-0000042");
        Choose(page, "own-fleet");

        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Transport Order TO-0000042 is planned as Shipment SH-0000012, carried by Own fleet.",
            "Thu 1 Oct: Over capacity, 34 of 30 pallet places after this assignment"));
    }

    [Fact]
    public void Assign_AnOrderWhoseRatingFailed_AssignsIt()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042", ratingResult: RatingResult.RatingFailed));
        Commands.AssignAsync("TO-0000042", "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        Select(page, "TO-0000042");
        Choose(page, Samples.Kessler.Id);

        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().ContainSingle().Which.Should().StartWith("Transport Order TO-0000042 is planned"));
    }

    [Fact]
    public void Assign_WhenTheTmsRefusesOne_SaysWhyAndStillAssignsTheOthers()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043"), Samples.ToPlan("TO-0000044"));
        Commands.AssignAsync("TO-0000042", "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000012"));
        Commands.AssignAsync("TO-0000043", "CAR-0002", Arg.Any<CancellationToken>())
            .Returns(new AssignAnswer.Refused("Transport Order TO-0000043 is Planned already."));
        Commands.AssignAsync("TO-0000044", "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000013"));
        SelectAll(page, "TO-0000042", "TO-0000043", "TO-0000044");
        Choose(page, Samples.Kessler.Id);

        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Transport Order TO-0000042 is planned as Shipment SH-0000012, carried by Kessler Spedition.",
            "Transport Order TO-0000043 wasn't assigned: Transport Order TO-0000043 is Planned already.",
            "Transport Order TO-0000044 is planned as Shipment SH-0000013, carried by Kessler Spedition."));
        page.Find("li[role=alert]").TextContent.Should().Contain("TO-0000043");
    }

    [Fact]
    public void Assign_WhenTheTmsCantBeReachedForOne_ShowsAnErrorToTryAgainAndStillAssignsTheOthers()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043"));
        Commands.AssignAsync("TO-0000042", "CAR-0002", Arg.Any<CancellationToken>())
            .Returns<AssignAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));
        Commands.AssignAsync("TO-0000043", "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Planned("SH-0000013"));
        SelectAll(page, "TO-0000042", "TO-0000043");
        Choose(page, Samples.Kessler.Id);

        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Transport Order TO-0000042 couldn't be assigned. Try again.",
            "Transport Order TO-0000043 is planned as Shipment SH-0000013, carried by Kessler Spedition."));
        // The one that failed stays selected, so the Planner can send it again.
        AssignButton(page).TextContent.Trim().Should().Be("Assign selected (1)");
    }

    [Fact]
    public void Assign_WhileTheTmsIsAnswering_CantBeSentAgain()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"));
        var answer = new TaskCompletionSource<AssignAnswer>();
        Commands.AssignAsync("TO-0000042", "CAR-0002", Arg.Any<CancellationToken>()).Returns(answer.Task);
        Select(page, "TO-0000042");
        Choose(page, Samples.Kessler.Id);

        AssignButton(page).Click();

        page.WaitForAssertion(() => AssignButton(page).HasAttribute("disabled").Should().BeTrue());
        answer.SetResult(new AssignAnswer.Refused("Carrier CAR-0002 is not active."));
        page.WaitForAssertion(() => AssignButton(page).HasAttribute("disabled").Should().BeFalse());
    }

    [Fact]
    public void Assign_AgainAfterwards_ShowsOnlyTheNewOutcomes()
    {
        var page = RenderWith(Samples.ToPlan("TO-0000042"), Samples.ToPlan("TO-0000043"));
        Commands.AssignAsync(Arg.Any<string>(), "CAR-0002", Arg.Any<CancellationToken>()).Returns(new AssignAnswer.Refused("Carrier CAR-0002 is not active."));
        Select(page, "TO-0000042");
        Choose(page, Samples.Kessler.Id);
        AssignButton(page).Click();
        page.WaitForAssertion(() => Outcomes(page).Should().ContainSingle());
        Select(page, "TO-0000042", selected: false);
        Select(page, "TO-0000043");

        AssignButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal("Transport Order TO-0000043 wasn't assigned: Carrier CAR-0002 is not active."));
    }

    private IRenderedComponent<WaitingForPlanning> RenderWith(params TransportOrderToPlanSummary[] transportOrders)
    {
        Queries.ListWaitingForPlanningAsync(Arg.Any<CancellationToken>()).Returns(transportOrders);
        var page = Render<WaitingForPlanning>();
        page.WaitForElement($"a[href='/transport-orders/{transportOrders[0].TransportOrderNumber}']");
        return page;
    }

    // Fluent UI's checkbox takes its new value after the event returns, so this waits until it shows it.
    private static void Select(IRenderedComponent<WaitingForPlanning> page, string number, bool selected = true)
    {
        var checkbox = $"fluent-checkbox[aria-label='Select {number}']";
        page.Find(checkbox).TriggerEvent("oncheckedchange", new CheckboxChangeEventArgs { Checked = selected });
        page.WaitForAssertion(() => page.Find(checkbox).ClassList.Contains("checked").Should().Be(selected));
    }

    private static void SelectAll(IRenderedComponent<WaitingForPlanning> page, params string[] numbers)
    {
        foreach (var number in numbers)
        {
            Select(page, number);
        }
    }

    // Chooses who carries them in the picker: "own-fleet", or a Carrier's ID.
    private static void Choose(IRenderedComponent<WaitingForPlanning> page, string value) =>
        page.WaitForElement($"fluent-select fluent-option[value='{value}']").ParentElement!.Change(value);

    private static IElement AssignGroup(IRenderedComponent<WaitingForPlanning> page) => page.Find("[role=group][aria-label=Assign]");

    private static IElement AssignButton(IRenderedComponent<WaitingForPlanning> page) => AssignGroup(page).QuerySelector("fluent-button")!;

    // Each outcome's message, as it reads without its icon.
    private static List<string> Outcomes(IRenderedComponent<WaitingForPlanning> page) =>
        [.. page.FindAll("ul[aria-label='Assignment outcomes'] li .fluent-messagebar-message")
            .Select(message => string.Join(' ', message.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];

    // The last cell but one of the Transport Order's row: the last is the Suggest button.
    private static string RatingResultOf(IRenderedComponent<WaitingForPlanning> page, string number) =>
        page.Find($"tr:has(a[href='/transport-orders/{number}'])").QuerySelectorAll("td")[^2].TextContent.Trim();
}
