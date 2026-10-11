using AngleSharp.Dom;
using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.Shipments.ListToPutOnTrips;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Application.Trips;
using Norvia.Tms.Domain.Trips;
using Norvia.Tms.Web.Trips;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Trips;

/// <summary>The Planner's Shipments to put on Trips (P11): the list, the day filter, creating a Trip from the selected Shipments and adding them to a Draft.</summary>
public sealed class ShipmentsToPutOnTripsTests : TmsWebContext
{
    private static readonly DateOnly Wednesday = new(2026, 10, 14);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    private static readonly string[] Both = ["SH-0000001", "SH-0000002"];

    private static readonly ShipmentToPutOnTripSummary First = Shipment("SH-0000001", Wednesday, 6, 2_400m);
    private static readonly ShipmentToPutOnTripSummary Second = Shipment("SH-0000002", Wednesday, 2, 500m);
    private static readonly ShipmentToPutOnTripSummary Thursday = Shipment("SH-0000003", Wednesday.AddDays(1), 1, 100m);

    public ShipmentsToPutOnTripsTests()
    {
        Queries.ListShipmentsToPutOnTripsAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns([First, Second, Thursday]);
        Queries.ListTripsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Trip("TR-0000001", Wednesday, TripStatus.Draft),
            Trip("TR-0000002", Wednesday, TripStatus.Dispatched),
            Trip("TR-0000003", Wednesday.AddDays(1), TripStatus.Draft),
        ]);
        Queries.ListPlanningDaysAsync(Arg.Any<CancellationToken>()).Returns(
            [new PlanningDayView(Wednesday, 2, [], new DayLoadView(0, 0, 0, 0, false)), new PlanningDayView(Wednesday.AddDays(1), 2, [], new DayLoadView(0, 0, 0, 0, false))]);
    }

    [Fact]
    public void Render_ListsEachShipmentWithItsPlacesWindowsWeightAndPallets()
    {
        var page = Render<ShipmentsToPutOnTrips>();

        var cells = Cells(page, "SH-0000001");
        cells[1].Should().Be("SH-0000001");
        cells[2].Should().Be("Brouwer Dranken");
        cells[3].Should().Contain("Waalhaven Oostzijde 88, 3087 BM Rotterdam").And.Contain("Wed 14 Oct 2026, 08:00–12:00");
        cells[4].Should().Contain("Oudegracht 112, 3511 AW Utrecht").And.Contain("13:00–17:00");
        cells[5].Should().Be("2,400 kg");
        cells[6].Should().Be("6");
    }

    [Fact]
    public void Render_WithNoShipments_SaysNoneAreWaiting()
    {
        Queries.ListShipmentsToPutOnTripsAsync(Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>()).Returns([]);

        var page = Render<ShipmentsToPutOnTrips>();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("No Released Own fleet Shipments are waiting for a Trip."));
    }

    [Fact]
    public void FilteringByDay_ReadsTheShipmentsOfThatDay()
    {
        var page = Render<ShipmentsToPutOnTrips>();
        page.WaitForElement("fluent-checkbox[aria-label='Select SH-0000001']");

        page.Find("fluent-select[aria-label='Collection day']").Change("2026-10-15");

        page.WaitForAssertion(() => Queries.Received().ListShipmentsToPutOnTripsAsync(Wednesday.AddDays(1), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void CreateTrip_SendsTheSelectedShipmentsForTheirCollectionDay_AndSaysWhatWasCreated()
    {
        Commands.CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Created("TR-0000009"));
        var page = Render<ShipmentsToPutOnTrips>();
        SelectAll(page, "SH-0000001", "SH-0000002");

        CreateButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).CreateDraftTripAsync(
            Arg.Is<TripEntry>(entry => entry.Day == Wednesday && entry.VehicleId == null && entry.DriverId == null && entry.ShipmentNumbers!.SequenceEqual(Both)),
            Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => Outcome(page).Should().Contain("Draft Trip TR-0000009 created for Wed 14 Oct with 2 Shipment(s)."));
    }

    [Fact]
    public void CreateTrip_ForShipmentsOfDifferentDays_IsNotSent_AndSaysATripRunsOnOneDay()
    {
        var page = Render<ShipmentsToPutOnTrips>();
        SelectAll(page, "SH-0000001", "SH-0000003");

        CreateButton(page).Click();

        page.WaitForAssertion(() => Outcome(page).Should().Contain("A Trip runs on one day"));
        Commands.DidNotReceive().CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateTrip_AWarningFromTheTms_IsShownBesideTheSuccess()
    {
        Commands.CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>())
            .Returns(new TripAnswer.Created("TR-0000009", [new TripWarning(TripWarning.DeliveryWindowOutsideTripDay, "Shipment SH-0000001's delivery window doesn't include the Trip's day 2026-10-14.")]));
        var page = Render<ShipmentsToPutOnTrips>();
        SelectAll(page, "SH-0000001");

        CreateButton(page).Click();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Contain("delivery window doesn't include the Trip's day"));
    }

    [Fact]
    public void CreateTrip_Refused_SaysWhy_AndKeepsTheSelection()
    {
        Commands.CreateDraftTripAsync(Arg.Any<TripEntry>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Refused("Shipment SH-0000001 is on Trip TR-0000001 already."));
        var page = Render<ShipmentsToPutOnTrips>();
        SelectAll(page, "SH-0000001");

        CreateButton(page).Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("No Trip was created: Shipment SH-0000001 is on Trip TR-0000001 already."));
        CreateButton(page).TextContent.Should().Contain("(1)");
    }

    [Fact]
    public void AddToTrip_OffersTheDraftAndDispatchedTripsOfTheSelectedShipmentsDay_AndSendsTheSelectedShipments()
    {
        Commands.AddShipmentsToTripAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new TripAnswer.Added([]));
        var page = Render<ShipmentsToPutOnTrips>();
        SelectAll(page, "SH-0000001", "SH-0000002");

        page.WaitForAssertion(() => Group(page).QuerySelectorAll("fluent-option").Select(option => option.TextContent.Trim()).Should().Equal(
            "Choose a Trip", "TR-0000001 · 0 kg · 0 pallets", "TR-0000002 · 0 kg · 0 pallets"));
        Group(page).QuerySelector("fluent-select")!.Change("TR-0000001");
        AddButton(page).Click();

        page.WaitForAssertion(() => Commands.Received(1).AddShipmentsToTripAsync(
            "TR-0000001", Arg.Is<IReadOnlyList<string>>(numbers => numbers.SequenceEqual(Both)), Arg.Any<CancellationToken>()));
        page.WaitForAssertion(() => Outcome(page).Should().Contain("2 Shipment(s) added to Trip TR-0000001."));
    }

    [Fact]
    public void AddToTrip_Refused_SaysWhichRuleBrokeAndWhy()
    {
        Commands.AddShipmentsToTripAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new TripAnswer.Refused("The Trip's 2,900 kg is more than the Vehicle's 1,200 kg payload."));
        var page = Render<ShipmentsToPutOnTrips>();
        SelectAll(page, "SH-0000001");
        page.WaitForAssertion(() => Group(page).QuerySelectorAll("fluent-option").Should().HaveCount(3));
        Group(page).QuerySelector("fluent-select")!.Change("TR-0000001");

        AddButton(page).Click();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Should().Contain("Nothing was added to Trip TR-0000001: The Trip's 2,900 kg is more than the Vehicle's 1,200 kg payload."));
    }

    private static ShipmentToPutOnTripSummary Shipment(string number, DateOnly day, int pallets, decimal weightKg) => new(
        number,
        "TO-0000001",
        new ShipperView("SHP-0001", "Brouwer Dranken"),
        day,
        "Waalhaven Oostzijde 88, 3087 BM Rotterdam",
        new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), Cest),
        new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), Cest),
        "Oudegracht 112, 3511 AW Utrecht",
        new DateTimeOffset(day.ToDateTime(new TimeOnly(13, 0)), Cest),
        new DateTimeOffset(day.ToDateTime(new TimeOnly(17, 0)), Cest),
        weightKg,
        pallets);

    private static TripView Trip(string number, DateOnly day, TripStatus status) => new(number, day, status, null, null, null, null, null, null);

    private static List<string> Cells(IRenderedComponent<ShipmentsToPutOnTrips> page, string number) =>
        [.. page.WaitForElement($"tr:has(a[href='/shipments/{number}'])").QuerySelectorAll("td").Select(cell => cell.TextContent.Trim())];

    private static IElement Group(IRenderedComponent<ShipmentsToPutOnTrips> page) => page.WaitForElement("[role=group][aria-label='Put on a Trip']");

    private static IElement CreateButton(IRenderedComponent<ShipmentsToPutOnTrips> page) => Group(page).QuerySelectorAll("fluent-button")[0];

    private static IElement AddButton(IRenderedComponent<ShipmentsToPutOnTrips> page) => Group(page).QuerySelectorAll("fluent-button")[1];

    private static string Outcome(IRenderedComponent<ShipmentsToPutOnTrips> page) => page.Find("[role=status], [role=alert]").TextContent;

    private static void SelectAll(IRenderedComponent<ShipmentsToPutOnTrips> page, params string[] numbers)
    {
        foreach (var number in numbers)
        {
            var checkbox = $"fluent-checkbox[aria-label='Select {number}']";
            page.WaitForElement(checkbox).TriggerEvent("oncheckedchange", new CheckboxChangeEventArgs { Checked = true });
            page.WaitForAssertion(() => page.Find(checkbox).ClassList.Contains("checked").Should().BeTrue());
        }
    }
}
