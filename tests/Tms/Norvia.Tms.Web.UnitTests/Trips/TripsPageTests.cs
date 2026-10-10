using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Application.Trips.Choices;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.Trips;
using Norvia.Tms.Web.UnitTests.TransportOrders;
using NSubstitute.ExceptionExtensions;

namespace Norvia.Tms.Web.UnitTests.Trips;

/// <summary>The Planner's Trips view (P11): the Trips, creating a Draft, picking its Vehicle and Driver, and discarding it.</summary>
public sealed class TripsPageTests : TmsWebContext
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly TripView Empty = new("TR-0000001", Wednesday, TripStatus.Draft, null, null, null, null, null, null);
    private static readonly TripView Full = new("TR-0000002", Wednesday.AddDays(1), TripStatus.Draft, "VEH-0001", "VN-101-A", "DRV-0001", "Anouk de Vries", new TimeOnly(7, 30), TimeSpan.FromMinutes(390));
    private static readonly TripView Sent = new("TR-0000003", Wednesday.AddDays(2), TripStatus.Dispatched, "VEH-0002", "VN-102-B", "DRV-0002", "Joost Bakker", null, null);

    public TripsPageTests()
    {
        Queries.ListTripsAsync(Arg.Any<CancellationToken>()).Returns([Empty, Full, Sent]);
        Queries.ListPlanningDaysAsync(Arg.Any<CancellationToken>()).Returns(
            [new PlanningDayView(Wednesday, 2, [], new DayLoadView(0, 0, 0, 0, false))]);
        Queries.ListTripChoicesAsync(Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TripChoices([new VehicleChoice("VEH-0001", "VN-101-A")], [new DriverChoice("DRV-0001", "Anouk de Vries")]));
    }

    [Fact]
    public void ListsTheTripsWithStatusVehicleAndDriver_AndOffersActionsOnlyOnDrafts()
    {
        var page = Render<TripsPage>();

        var rows = page.WaitForElements("tbody tr");
        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("TR-0000001").And.Contain("Draft").And.Contain("—");
        rows[1].TextContent.Should().Contain("VN-101-A").And.Contain("Anouk de Vries");
        rows[2].TextContent.Should().Contain("Dispatched");
        rows[2].QuerySelectorAll("fluent-button").Should().BeEmpty("a Dispatched Trip is never discarded or picked for");
    }

    [Fact]
    public void ListsThePlannedStartAndDrivingTime_AndADashWhenThereIsNone()
    {
        var page = Render<TripsPage>();

        var rows = page.WaitForElements("tbody tr");
        rows[1].TextContent.Should().Contain("07:30").And.Contain("6:30");
        rows[0].TextContent.Should().NotContain("07:30");
    }

    [Fact]
    public void Pick_SendsThePlannedStartAndDrivingTimeTheTripAlreadyHas_AndWhatThePlannerTyped()
    {
        Commands.ChangeDraftTripAsync(Arg.Any<string>(), Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Done());
        var page = Render<TripsPage>();
        page.WaitForElement("fluent-button[aria-label='Plan TR-0000002']").Click();
        var form = page.WaitForElement("section[aria-label='Plan TR-0000002']");
        var fields = form.QuerySelectorAll("fluent-text-field");
        fields[0].GetAttribute("current-value").Should().Be("07:30");
        fields[1].GetAttribute("current-value").Should().Be("6:30");
        fields[0].Change("08:15");
        fields[1].Change("9:00");

        Click(form, "Save");

        page.WaitForAssertion(() => Commands.Received(1).ChangeDraftTripAsync(
            "TR-0000002", new TripEntry(Wednesday.AddDays(1), "VEH-0001", "DRV-0001", new TimeOnly(8, 15), TimeSpan.FromHours(9)), Arg.Any<CancellationToken>()));
    }

    [Theory]
    [InlineData("7h", "6:30")]
    [InlineData("07:30", "0:00")]
    [InlineData("07:30", "25:00")]
    [InlineData("07:30", "6:5")]
    public void Pick_ATimeThatIsNoTime_IsNotSent_AndSaysWhatToEnter(string start, string duration)
    {
        var page = Render<TripsPage>();
        page.WaitForElement("fluent-button[aria-label='Plan TR-0000002']").Click();
        var form = page.WaitForElement("section[aria-label='Plan TR-0000002']");
        var fields = form.QuerySelectorAll("fluent-text-field");
        fields[0].Change(start);
        fields[1].Change(duration);

        Click(form, "Save");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("hours and minutes"));
        Commands.DidNotReceive().ChangeDraftTripAsync(Arg.Any<string>(), Arg.Any<TripEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ANewTripInTheList_AppearsAfterTheNextRead_WithoutARefresh()
    {
        var page = Render<TripsPage>();
        page.WaitForElements("tbody tr").Should().HaveCount(3);
        Queries.ListTripsAsync(Arg.Any<CancellationToken>()).Returns([Empty, Full, Sent, Empty with { Number = "TR-0000004" }]);

        Time.Advance(RefreshInterval);

        page.WaitForAssertion(() => page.FindAll("tbody tr").Should().HaveCount(4));
    }

    [Fact]
    public void Create_SendsTheChosenDayVehicleAndDriver_AndSaysWhatWasCreated()
    {
        Commands.CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Created("TR-0000009"));
        var page = Render<TripsPage>();
        var form = page.WaitForElement("section[aria-label='Create a Draft Trip']");
        form.QuerySelectorAll("fluent-select")[0].Change("2026-10-14");
        page.WaitForAssertion(() => page.FindAll("section[aria-label='Create a Draft Trip'] fluent-option").Should().Contain(option => option.TextContent.Contains("VN-101-A", StringComparison.Ordinal)));
        form.QuerySelectorAll("fluent-select")[1].Change("VEH-0001");
        form.QuerySelectorAll("fluent-select")[2].Change("DRV-0001");

        Click(form, "Create Trip");

        page.WaitForAssertion(() => Commands.Received(1).CreateDraftTripAsync(new TripEntry(Wednesday, "VEH-0001", "DRV-0001"), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => form.QuerySelector("[role=status]")!.TextContent.Should().Contain("Draft Trip TR-0000009 created for Wed 14 Oct."));
    }

    [Fact]
    public void Create_WithoutADay_IsNotSent()
    {
        var page = Render<TripsPage>();
        var form = page.WaitForElement("section[aria-label='Create a Draft Trip']");

        Click(form, "Create Trip");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Choose a day."));
        Commands.DidNotReceive().CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_WhenTheTmsRefuses_ShowsItsReason()
    {
        Commands.CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Refused("Vehicle VEH-0001 is not available."));
        var page = Render<TripsPage>();
        var form = page.WaitForElement("section[aria-label='Create a Draft Trip']");
        form.QuerySelectorAll("fluent-select")[0].Change("2026-10-14");

        Click(form, "Create Trip");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("Not saved: Vehicle VEH-0001 is not available."));
    }

    [Fact]
    public void Create_WhenTheTmsCantBeReached_SaysToTryAgain()
    {
        Commands.CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));
        var page = Render<TripsPage>();
        var form = page.WaitForElement("section[aria-label='Create a Draft Trip']");
        form.QuerySelectorAll("fluent-select")[0].Change("2026-10-14");

        Click(form, "Create Trip");

        page.WaitForAssertion(() => form.QuerySelector("[role=alert]")!.TextContent.Should().Contain("The TMS couldn't save that. Try again."));
    }

    [Fact]
    public void Pick_OpensTheTripsFormWithItsChoices_AndSendsTheVehicleAndDriver()
    {
        Commands.ChangeDraftTripAsync(Arg.Any<string>(), Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Done());
        var page = Render<TripsPage>();
        page.WaitForElement("fluent-button[aria-label='Plan TR-0000001']").Click();
        var form = page.WaitForElement("section[aria-label='Plan TR-0000001']");
        page.WaitForAssertion(() => Queries.Received().ListTripChoicesAsync(Wednesday, "TR-0000001", Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => form.QuerySelectorAll("fluent-option").Should().Contain(option => option.TextContent.Contains("VN-101-A", StringComparison.Ordinal)));
        form.QuerySelectorAll("fluent-select")[0].Change("VEH-0001");
        form.QuerySelectorAll("fluent-select")[1].Change("DRV-0001");

        Click(form, "Save");

        page.WaitForAssertion(() => Commands.Received(1).ChangeDraftTripAsync("TR-0000001", new TripEntry(Wednesday, "VEH-0001", "DRV-0001"), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => page.FindAll("section[aria-label='Plan TR-0000001']").Should().BeEmpty("the form closes once the TMS took the change"));
    }

    [Fact]
    public void Pick_Cancel_ClosesTheForm()
    {
        var page = Render<TripsPage>();
        page.WaitForElement("fluent-button[aria-label='Plan TR-0000002']").Click();
        var form = page.WaitForElement("section[aria-label='Plan TR-0000002']");

        Click(form, "Cancel");

        page.WaitForAssertion(() => page.FindAll("section[aria-label='Plan TR-0000002']").Should().BeEmpty());
    }

    [Fact]
    public void Discard_RemovesTheDraft_AndSaysSo_OrShowsTheRefusal_OrThatTheTmsCantBeReached()
    {
        var page = Render<TripsPage>();
        var button = "fluent-button[aria-label='Discard TR-0000001']";
        Commands.DiscardDraftTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripAnswer.Refused("Trip TR-0000001 is Dispatched."));
        page.WaitForElement(button).Click();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Not discarded: Trip TR-0000001 is Dispatched."));

        Commands.DiscardDraftTripAsync("TR-0000001", Arg.Any<CancellationToken>()).Returns(new TripAnswer.Done());
        page.Find(button).Click();
        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("Draft Trip TR-0000001 discarded."));

        Commands.DiscardDraftTripAsync("TR-0000001", Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("down"));
        page.Find(button).Click();
        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("The TMS couldn't discard the Trip."));
    }

    [Fact]
    public void ListsNoTrips_SaysSo()
    {
        Queries.ListTripsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<TripsPage>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("There are no Trips yet."));
    }

    private static void Click(AngleSharp.Dom.IElement scope, string label) =>
        scope.QuerySelectorAll("fluent-button").Single(button => button.TextContent.Trim() == label).Click();

    [Fact]
    public void ListsTheStopCountAndTheLoadAgainstTheVehiclesCapacity_AndLinksToTheTripDetail()
    {
        Queries.ListTripsAsync(Arg.Any<CancellationToken>()).Returns(
            [Full with { StopCount = 4, LoadWeightKg = 650m, LoadPalletPlaces = 3, PayloadKg = 1_200, VehiclePalletPlaces = 4 }, Empty]);

        var page = Render<TripsPage>();

        var rows = page.WaitForElements("tbody tr");
        rows[0].TextContent.Should().Contain("4").And.Contain("650 / 1,200 kg · 3 / 4 pallets");
        rows[0].QuerySelector("a[href='/trips/TR-0000002']").Should().NotBeNull();
        rows[1].TextContent.Should().Contain("0 kg · 0 pallets");
    }
}
