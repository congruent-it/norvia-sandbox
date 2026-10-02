using AngleSharp.Dom;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.Shipments.ListToRelease;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Shipments;

public sealed class ShipmentsToReleaseTests : TmsWebContext
{
    public ShipmentsToReleaseTests()
    {
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns([Samples.Kessler, Samples.VanDijk]);
    }

    [Fact]
    public void Render_BeforeTheShipmentsAreRead_ShowsLoading()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<ShipmentToReleaseSummary>>();
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = Render<ShipmentsToRelease>();

        page.Find("[role=status]").TextContent.Should().Contain("Loading the Shipments to release");
    }

    [Fact]
    public void Render_WithShipmentsToRelease_ListsEachWithShipperTransportOrderCarriedByAndPlanned()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns([Samples.ToRelease("SH-0000012", "TO-0000042")]);

        var page = Render<ShipmentsToRelease>();

        var row = page.WaitForElement("tr:has(a[href='/shipments/SH-0000012'])");
        var cells = row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        cells[1].Should().Be("SH-0000012");
        cells[2].Should().Be("TO-0000042");
        cells[3].Should().Be("Brouwer Dranken");
        cells[4].Should().Be("Kessler Spedition");
        cells[5].Should().Be("Thu 24 Sep 2026, 11:45");
    }

    [Fact]
    public void Render_AShipmentThatCameBackFromACarrier_ShowsItsLastCarrierBookingOutcomeAndReason()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Samples.ToRelease("SH-0000012") with
            {
                LastCarrierBookingOutcome = new LastCarrierBookingOutcome(
                    CarrierBookingStatus.Rejected, Samples.Kessler, CarrierBookingRejectionReason.NoCapacity, "Fully booked until Friday."),
            },
            Samples.ToRelease("SH-0000013"),
        ]);

        var page = Render<ShipmentsToRelease>();

        page.WaitForAssertion(() =>
        {
            CellsOf(page, "SH-0000012")[6].Should().Be("Rejected by Kessler Spedition: Fully booked until Friday.");
            CellsOf(page, "SH-0000013")[6].Should().BeEmpty();
        });
    }

    [Fact]
    public void Render_WithShipmentsToRelease_ListsThemInTheOrderTheQueryGives()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>())
            .Returns([Samples.ToRelease("SH-0000013"), Samples.ToRelease("SH-0000012")]);

        var page = Render<ShipmentsToRelease>();

        page.WaitForAssertion(() => page.FindAll("a[href^='/shipments/']").Select(link => link.TextContent)
            .Should().Equal("SH-0000013", "SH-0000012"));
    }

    [Fact]
    public void Render_WithNoShipmentsToRelease_SaysSoInsteadOfAnEmptyTable()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<ShipmentsToRelease>();

        page.WaitForAssertion(() =>
            page.Find("[role=status]").TextContent.Should().Contain("No Shipments are waiting to be released."));
        page.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenAShipmentIsPlannedWhileTheListIsOpen_ShowsItWithoutARefresh()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(
            [Samples.ToRelease("SH-0000012")],
            [Samples.ToRelease("SH-0000012"), Samples.ToRelease("SH-0000013")]);
        var page = Render<ShipmentsToRelease>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000013']");
    }

    [Fact]
    public void Render_WhenAReleasedShipmentIsNoLongerToRelease_DropsItWithoutARefresh()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(
            [Samples.ToRelease("SH-0000012"), Samples.ToRelease("SH-0000013")],
            [Samples.ToRelease("SH-0000013")]);
        var page = Render<ShipmentsToRelease>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("a[href='/shipments/SH-0000012']").Should().BeEmpty());
        page.Find("a[href='/shipments/SH-0000013']");
    }

    [Fact]
    public void Render_WhenTheShipmentsCantBeRead_ShowsAnErrorAndTriesAgain()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new TimeoutException("The database didn't answer."),
            _ => Task.FromResult<IReadOnlyList<ShipmentToReleaseSummary>>([Samples.ToRelease("SH-0000012")]));

        var page = Render<ShipmentsToRelease>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The Shipments to release couldn't be loaded. Trying again…"));

        Time.Advance(RefreshInterval);

        page.WaitForElement("a[href='/shipments/SH-0000012']");
        page.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenARefreshFails_KeepsTheShipmentsAlreadyShownAndWarnsTheyMayBeOutOfDate()
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult<IReadOnlyList<ShipmentToReleaseSummary>>([Samples.ToRelease("SH-0000012")]),
            _ => throw new TimeoutException("The database didn't answer."));
        var page = Render<ShipmentsToRelease>();
        page.WaitForElement("a[href='/shipments/SH-0000012']");

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("The list couldn't be refreshed and may be out of date. Trying again…"));
        page.Find("a[href='/shipments/SH-0000012']");
    }

    [Fact]
    public void Picker_OffersOwnFleetAndOnlyTheActiveCarriers()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));

        page.WaitForAssertion(() => ReassignPicker(page, "SH-0000012").QuerySelectorAll("fluent-option").Select(option => option.TextContent.Trim())
            .Should().Equal("Choose who carries it", "Own fleet", "Kessler Spedition", "Van Dijk Transport"));
    }

    [Fact]
    public void Picker_WhileTheCarriersAreRead_SaysSo()
    {
        var neverAnswered = new TaskCompletionSource<IReadOnlyList<CarrierView>>();
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns(call => neverAnswered.Task.WaitAsync(call.Arg<CancellationToken>()));

        var page = RenderWith(Samples.ToRelease("SH-0000012"));

        page.WaitForAssertion(() => ReleaseGroup(page).QuerySelector("[role=status]")!.TextContent.Should().Contain("Loading the Carriers…"));
    }

    [Fact]
    public void Picker_WhenTheCarriersCantBeRead_OffersOwnFleetAndSaysWhy()
    {
        Queries.ListActiveCarriersAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CarrierView>>(_ => throw new TimeoutException("The database didn't answer."));

        var page = RenderWith(Samples.ToRelease("SH-0000012"));

        page.WaitForAssertion(() => ReleaseGroup(page).QuerySelector("[role=alert]")!.TextContent.Should()
            .Contain("The Carriers couldn't be loaded, so a Shipment can only be reassigned to Own fleet."));
        page.WaitForAssertion(() => ReassignPicker(page, "SH-0000012").QuerySelectorAll("fluent-option").Select(option => option.TextContent.Trim())
            .Should().Equal("Choose who carries it", "Own fleet"));
    }

    [Fact]
    public void Release_WithNothingSelected_CantBeSent()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));

        ReleaseButton(page).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Release_ToSeveralSelected_ReleasesEachAndSaysSo()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"), Samples.ToRelease("SH-0000013"), Samples.ToRelease("SH-0000014"));
        Commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());
        Commands.ReleaseAsync("SH-0000014", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());
        Select(page, "SH-0000012");
        Select(page, "SH-0000014");

        ReleaseButton(page).TextContent.Trim().Should().Be("Release selected (2)");
        ReleaseButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Shipment SH-0000012 is released.",
            "Shipment SH-0000014 is released."));
        Commands.DidNotReceive().ReleaseAsync("SH-0000013", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Release_WhenTheTmsRefusesOne_SaysWhyAndStillReleasesTheOthers()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"), Samples.ToRelease("SH-0000013"), Samples.ToRelease("SH-0000014"));
        Commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());
        Commands.ReleaseAsync("SH-0000013", Arg.Any<CancellationToken>())
            .Returns(new ReleaseAnswer.Refused("Shipment SH-0000013 isn't Planned."));
        Commands.ReleaseAsync("SH-0000014", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());
        SelectAll(page, "SH-0000012", "SH-0000013", "SH-0000014");

        ReleaseButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Shipment SH-0000012 is released.",
            "Shipment SH-0000013 wasn't released: Shipment SH-0000013 isn't Planned.",
            "Shipment SH-0000014 is released."));
        page.Find("li[role=alert]").TextContent.Should().Contain("SH-0000013");
    }

    [Fact]
    public void Release_WhenTheTmsCantBeReachedForOne_ShowsAnErrorToTryAgainAndStillReleasesTheOthers()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"), Samples.ToRelease("SH-0000013"));
        Commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>())
            .Returns<ReleaseAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));
        Commands.ReleaseAsync("SH-0000013", Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Released());
        SelectAll(page, "SH-0000012", "SH-0000013");

        ReleaseButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal(
            "Shipment SH-0000012 couldn't be released. Try again.",
            "Shipment SH-0000013 is released."));
        // The one that failed stays selected, so the Planner can send it again.
        ReleaseButton(page).TextContent.Trim().Should().Be("Release selected (1)");
    }

    [Fact]
    public void Release_WhileTheTmsIsAnswering_CantBeSentAgain()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));
        var answer = new TaskCompletionSource<ReleaseAnswer>();
        Commands.ReleaseAsync("SH-0000012", Arg.Any<CancellationToken>()).Returns(answer.Task);
        Select(page, "SH-0000012");

        ReleaseButton(page).Click();

        page.WaitForAssertion(() => ReleaseButton(page).HasAttribute("disabled").Should().BeTrue());
        answer.SetResult(new ReleaseAnswer.Refused("Shipment SH-0000012 isn't Planned."));
        page.WaitForAssertion(() => ReleaseButton(page).HasAttribute("disabled").Should().BeFalse());
    }

    [Fact]
    public void Release_AgainAfterwards_ShowsOnlyTheNewOutcomes()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"), Samples.ToRelease("SH-0000013"));
        Commands.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ReleaseAnswer.Refused("Shipment isn't Planned."));
        Select(page, "SH-0000012");
        ReleaseButton(page).Click();
        page.WaitForAssertion(() => Outcomes(page).Should().ContainSingle());
        Select(page, "SH-0000012", selected: false);
        Select(page, "SH-0000013");

        ReleaseButton(page).Click();

        page.WaitForAssertion(() => Outcomes(page).Should().Equal("Shipment SH-0000013 wasn't released: Shipment isn't Planned."));
    }

    [Fact]
    public void Reassign_WithNothingChosen_CantBeSent()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));

        ReassignButton(page, "SH-0000012").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Reassign_ToACarrier_UpdatesWhoCarriesItAndKeepsItsShipmentNumber()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012") with { CarriedBy = new CarriedByView(AssignedTo.Carrier, Samples.Kessler) });
        Commands.ReassignAsync("SH-0000012", Samples.VanDijk.Id, Arg.Any<CancellationToken>())
            .Returns(new ReassignAnswer.Reassigned(new CarriedByView(AssignedTo.Carrier, Samples.VanDijk)));
        Choose(page, "SH-0000012", Samples.VanDijk.Id);

        ReassignButton(page, "SH-0000012").Click();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should()
            .Contain("Shipment SH-0000012 is now carried by Van Dijk Transport."));
    }

    [Fact]
    public void Reassign_ToOwnFleet_ReassignsWithoutACarrier()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));
        Commands.ReassignAsync("SH-0000012", null, Arg.Any<CancellationToken>())
            .Returns(new ReassignAnswer.Reassigned(new CarriedByView(AssignedTo.OwnFleet, null)));
        Choose(page, "SH-0000012", "own-fleet");

        ReassignButton(page, "SH-0000012").Click();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should()
            .Contain("Shipment SH-0000012 is now carried by Own fleet."));
    }

    [Fact]
    public void Reassign_WhenTheTmsRefuses_SaysWhy()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));
        Commands.ReassignAsync("SH-0000012", Samples.VanDijk.Id, Arg.Any<CancellationToken>())
            .Returns(new ReassignAnswer.Refused("Shipment SH-0000012 is Released already."));
        Choose(page, "SH-0000012", Samples.VanDijk.Id);

        ReassignButton(page, "SH-0000012").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Shipment SH-0000012 wasn't reassigned: Shipment SH-0000012 is Released already."));
    }

    [Fact]
    public void Reassign_WhenTheTmsCantBeReached_ShowsAnErrorToTryAgain()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));
        Commands.ReassignAsync("SH-0000012", Samples.VanDijk.Id, Arg.Any<CancellationToken>())
            .Returns<ReassignAnswer>(_ => throw new HttpRequestException("The TMS API didn't answer."));
        Choose(page, "SH-0000012", Samples.VanDijk.Id);

        ReassignButton(page, "SH-0000012").Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should()
            .Contain("Shipment SH-0000012 couldn't be reassigned. Try again."));
    }

    [Fact]
    public void Reassign_WhileTheTmsIsAnswering_CantBeSentAgain()
    {
        var page = RenderWith(Samples.ToRelease("SH-0000012"));
        var answer = new TaskCompletionSource<ReassignAnswer>();
        Commands.ReassignAsync("SH-0000012", Samples.VanDijk.Id, Arg.Any<CancellationToken>()).Returns(answer.Task);
        Choose(page, "SH-0000012", Samples.VanDijk.Id);

        ReassignButton(page, "SH-0000012").Click();

        page.WaitForAssertion(() => ReassignButton(page, "SH-0000012").HasAttribute("disabled").Should().BeTrue());
        answer.SetResult(new ReassignAnswer.Refused("Shipment SH-0000012 is Released already."));
        page.WaitForAssertion(() => ReassignButton(page, "SH-0000012").HasAttribute("disabled").Should().BeFalse());
    }

    private IRenderedComponent<ShipmentsToRelease> RenderWith(params ShipmentToReleaseSummary[] shipments)
    {
        Queries.ListShipmentsToReleaseAsync(Arg.Any<CancellationToken>()).Returns(shipments);
        var page = Render<ShipmentsToRelease>();
        page.WaitForElement($"a[href='/shipments/{shipments[0].ShipmentNumber}']");
        return page;
    }

    // Fluent UI's checkbox takes its new value after the event returns, so this waits until it shows it.
    private static List<string> CellsOf(IRenderedComponent<ShipmentsToRelease> page, string shipmentNumber) =>
        [.. page.Find($"tr:has(a[href='/shipments/{shipmentNumber}'])").QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];

    private static void Select(IRenderedComponent<ShipmentsToRelease> page, string number, bool selected = true)
    {
        var checkbox = $"fluent-checkbox[aria-label='Select {number}']";
        page.Find(checkbox).TriggerEvent("oncheckedchange", new CheckboxChangeEventArgs { Checked = selected });
        page.WaitForAssertion(() => page.Find(checkbox).ClassList.Contains("checked").Should().Be(selected));
    }

    private static void SelectAll(IRenderedComponent<ShipmentsToRelease> page, params string[] numbers)
    {
        foreach (var number in numbers)
        {
            Select(page, number);
        }
    }

    // Chooses who carries a Shipment in its own row's picker: "own-fleet", or a Carrier's ID.
    private static void Choose(IRenderedComponent<ShipmentsToRelease> page, string shipmentNumber, string value) =>
        ReassignPicker(page, shipmentNumber).Change(value);

    private static IElement ReassignPicker(IRenderedComponent<ShipmentsToRelease> page, string shipmentNumber) =>
        page.WaitForElement($"fluent-select[aria-label='Reassign {shipmentNumber} to']");

    private static IElement ReassignButton(IRenderedComponent<ShipmentsToRelease> page, string shipmentNumber) =>
        page.WaitForElement($"fluent-button[aria-label='Reassign {shipmentNumber}']");

    private static IElement ReleaseGroup(IRenderedComponent<ShipmentsToRelease> page) => page.Find("[role=group][aria-label=Release]");

    private static IElement ReleaseButton(IRenderedComponent<ShipmentsToRelease> page) => ReleaseGroup(page).QuerySelector("fluent-button")!;

    // Each release outcome's message, as it reads without its icon.
    private static List<string> Outcomes(IRenderedComponent<ShipmentsToRelease> page) =>
        [.. page.FindAll("ul[aria-label='Release outcomes'] li .fluent-messagebar-message")
            .Select(message => string.Join(' ', message.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];
}
