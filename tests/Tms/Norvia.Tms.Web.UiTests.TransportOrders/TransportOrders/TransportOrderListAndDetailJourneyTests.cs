using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>
/// The Transport Orders list and detail page (#523). A Planner and Customer service browse every Transport Order the TMS accepted: filter by status and
/// Shipper, search, page, and open an order. The detail page links an order's Shipment and Trip, or says why it has none. The tests share one TMS, so each
/// tags the Shipper references it makes and searches for the tag.
/// </summary>
public sealed partial class TransportOrderListAndDetailJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    private const string PageSizeTag = "page";
    private readonly TmsUiFixture _tms = tms;
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    [Fact]
    public async Task ThePlanner_FiltersTheList_ByStatusAndShipper_AndSeesAcceptedAndPlannedOrders()
    {
        var tag = NewTag();
        var accepted = await SubmitAcceptedAsync(Bookings.Valid(Reference(tag, 1)));
        var groenrijk = await SubmitAcceptedAsync(WithShipper(Bookings.Valid(Reference(tag, 2)), "SHP-0003"));
        var planned = await SubmitAcceptedAsync(Bookings.Valid(Reference(tag, 3)));
        await OpenWaitingForPlanningAsync();
        await AssignToOwnFleetAsync(planned);

        await OpenTransportOrdersAsPlannerAsync();
        await SearchAsync(tag);

        await Expect(OrderRow(accepted)).ToContainTextAsync("Accepted");
        await Expect(OrderRow(groenrijk)).ToContainTextAsync("Tuincentrum Groenrijk");
        await Expect(OrderRow(planned)).ToContainTextAsync("Planned");
        await Expect(OrderRow(planned).GetByRole(AriaRole.Link, new() { NameRegex = new("^SH-") })).ToBeVisibleAsync();
        await Expect(Footer()).ToContainTextAsync("3 Transport Orders");

        await ChooseAsync("Status", "Planned");
        await Expect(OrderRow(planned)).ToBeVisibleAsync();
        await Expect(OrderRow(accepted)).ToHaveCountAsync(0);
        await Expect(OrderRow(groenrijk)).ToHaveCountAsync(0);
        await Expect(Footer()).ToContainTextAsync("1 Transport Orders");

        await ChooseAsync("Status", "Accepted");
        await Expect(OrderRow(accepted)).ToBeVisibleAsync();
        await Expect(OrderRow(groenrijk)).ToBeVisibleAsync();
        await Expect(OrderRow(planned)).ToHaveCountAsync(0);

        await ChooseAsync("Shipper", "Tuincentrum Groenrijk");
        await Expect(OrderRow(groenrijk)).ToBeVisibleAsync();
        await Expect(OrderRow(accepted)).ToHaveCountAsync(0);
        await Expect(Footer()).ToContainTextAsync("1 Transport Orders");

        await ChooseAsync("Status", "Planned");
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "No Transport Orders match the search and filters." })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ThePlanner_PagesThroughTheList_NewestFirst()
    {
        var tag = $"{PageSizeTag}{NewTag()}";
        var numbers = new List<string>();
        for (var index = 1; index <= 26; index++)
        {
            numbers.Add(await SubmitAcceptedAsync(Bookings.Valid(Reference(tag, index))));
        }

        await OpenTransportOrdersAsPlannerAsync();
        await SearchAsync(tag);

        await Expect(Footer()).ToContainTextAsync("Page 1 of 2 · 26 Transport Orders");
        await Expect(Rows()).ToHaveCountAsync(26); // 25 orders and the header row
        await Expect(OrderRow(numbers[^1])).ToBeVisibleAsync();
        await Expect(OrderRow(numbers[0])).ToHaveCountAsync(0);
        await Expect(Button("Previous")).ToBeDisabledAsync();

        await Button("Next").ClickAsync();

        await Expect(Footer()).ToContainTextAsync("Page 2 of 2 · 26 Transport Orders");
        await Expect(OrderRow(numbers[0])).ToBeVisibleAsync();
        await Expect(Rows()).ToHaveCountAsync(2);
        await Expect(Button("Next")).ToBeDisabledAsync();

        await Button("Previous").ClickAsync();

        await Expect(Footer()).ToContainTextAsync("Page 1 of 2");
        await Expect(OrderRow(numbers[^1])).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ThePlanner_SearchesByNumberShipperReferenceAndShipment_FindsTheOrder_AndOpensItsDetailFromTheRow()
    {
        var tag = NewTag();
        var other = await SubmitAcceptedAsync(Bookings.Valid(Reference(tag, 1)));
        var reference = Reference(tag, 2);
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference));
        await OpenWaitingForPlanningAsync();
        await AssignToOwnFleetAsync(number);

        await OpenTransportOrdersAsPlannerAsync();
        await SearchAsync(tag);
        var shipment = (await OrderRow(number).GetByRole(AriaRole.Link, new() { NameRegex = new("^SH-") }).TextContentAsync())!;
        await Expect(OrderRow(other)).ToBeVisibleAsync();

        await SearchAsync(number);
        await Expect(OrderRow(number)).ToBeVisibleAsync();
        await Expect(OrderRow(other)).ToHaveCountAsync(0);

        await SearchAsync(reference);
        await Expect(OrderRow(number)).ToBeVisibleAsync();
        await Expect(OrderRow(other)).ToHaveCountAsync(0);

        await SearchAsync(shipment.ToLower(CultureInfo.InvariantCulture));
        await Expect(OrderRow(number)).ToBeVisibleAsync();
        await Expect(OrderRow(other)).ToHaveCountAsync(0);

        await OrderRow(number).GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Region("Transport Order")).ToContainTextAsync(reference);
    }

    [Fact]
    public async Task CustomerService_OpensTheListAndAnOrdersDetail_FromItsOwnNavigation()
    {
        var reference = Reference(NewTag(), 1);
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();
        await OpenViewAsync("Transport Orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders", Level = 1 })).ToBeVisibleAsync();
        await SearchAsync(reference);
        await OrderRow(number).GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Region("Transport Order")).ToContainTextAsync("Accepted");
        await Expect(Region("Transport Order")).ToContainTextAsync("Brouwer Dranken");
    }

    [Fact]
    public async Task TheDetail_OfAPlannedOwnFleetOrderOnATrip_LinksTheShipmentAndTheTrip_AndShowsTheStops()
    {
        var day = await DemoTripDayAsync();
        var reference = Reference(NewTag(), 1);
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference).CollectedFrom(EightOClock(day)));
        await OpenWaitingForPlanningAsync();
        await AssignToOwnFleetAsync(number);
        var shipment = (await Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Link).TextContentAsync())!;

        await OpenViewAsync("Shipments to release");
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipment }).GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipment}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Release" }).GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status)).ToContainTextAsync($"Shipment {shipment} is released.");

        await OpenViewAsync("Shipments to put on Trips");
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Loading the" })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipment }).GetByRole(AriaRole.Checkbox).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Put on a Trip" }).GetByRole(AriaRole.Button, new() { Name = "Create Trip" }).ClickAsync();
        var created = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Draft Trip TR-" });
        await Expect(created).ToBeVisibleAsync();
        var trip = TripNumber().Match(await created.TextContentAsync() ?? string.Empty).Value;

        await OpenTransportOrdersAsPlannerAsync();
        await SearchAsync(reference);
        await OrderRow(number).GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Region("Shipment").GetByRole(AriaRole.Link, new() { Name = shipment })).ToBeVisibleAsync();
        await Expect(Region("Shipment")).ToContainTextAsync("Own fleet");
        var tripRegion = Region("Trip");
        await Expect(tripRegion.GetByRole(AriaRole.Link, new() { Name = trip })).ToBeVisibleAsync();
        var stops = tripRegion.GetByRole(AriaRole.Table, new() { Name = "Stops" });
        await Expect(stops.GetByRole(AriaRole.Row)).ToHaveCountAsync(3);
        await Expect(stops).ToContainTextAsync("Collect");
        await Expect(stops).ToContainTextAsync("Deliver");

        await tripRegion.GetByRole(AriaRole.Link, new() { Name = trip }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Trip {trip}", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheDetail_OfAnOwnFleetOrderOnNoTrip_SaysSo_AndLinksToTheShipmentsToPutOnTrips()
    {
        var reference = Reference(NewTag(), 1);
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference));
        await OpenWaitingForPlanningAsync();
        await AssignToOwnFleetAsync(number);

        await OpenTransportOrdersAsPlannerAsync();
        await SearchAsync(reference);
        await OrderRow(number).GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Region("Shipment")).ToContainTextAsync("Own fleet");
        await Expect(Region("Trip")).ToContainTextAsync("Not on a Trip yet.");

        await Region("Trip").GetByRole(AriaRole.Link, new() { Name = "Shipments to put on Trips" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Shipments to put on Trips", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TheDetail_OfAnOrderNotYetPlanned_ShowsTheEmptyState_AndLinksToTheOrdersWaitingForPlanning()
    {
        var reference = Reference(NewTag(), 1);
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference));

        await OpenTransportOrdersAsPlannerAsync();
        await SearchAsync(reference);
        await OrderRow(number).GetByRole(AriaRole.Link, new() { Name = number }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Region("Shipment")).ToContainTextAsync("No Shipment yet");
        await Expect(Region("Trip")).ToContainTextAsync("No Trip: there is no Shipment yet.");

        await Region("Shipment").GetByRole(AriaRole.Link, new() { Name = "Transport Orders waiting for planning" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders waiting for planning", Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ThePlanner_OpensOnTheHomePage_AndTheListIsOneMenuAway()
    {
        await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Home", Level = 1 })).ToBeVisibleAsync();
        await OpenViewAsync("Transport Orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders", Level = 1, Exact = true })).ToBeVisibleAsync();
    }

    private static string NewTag() => $"L{Guid.NewGuid():N}"[..12];

    private static string Reference(string tag, int index) => $"{tag}-{index}";

    private static JsonObject WithShipper(JsonObject booking, string shipperId)
    {
        booking["shipperId"] = shipperId;
        return booking;
    }

    private static DateTimeOffset EightOClock(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(8, 0));
        return new DateTimeOffset(local, Amsterdam.GetUtcOffset(local));
    }

    // The day of the demo's Shipments to put on Trips: the one open day an Own fleet Shipment can be put on a Trip for.
    private async Task<DateOnly> DemoTripDayAsync()
    {
        using var response = await _tms.Manage.GetAsync(new Uri("/v1/shipments/to-put-on-trips", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var shipments = await response.Content.ReadFromJsonAsync<JsonArray>(TestContext.Current.CancellationToken);
        var day = shipments!.Select(shipment => shipment!["collectionDay"]?.GetValue<string>()).First(day => day is not null)!;
        return DateOnly.Parse(day, CultureInfo.InvariantCulture);
    }

    private async Task OpenTransportOrdersAsPlannerAsync()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await OpenViewAsync("Transport Orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders", Level = 1, Exact = true })).ToBeVisibleAsync();
    }

    private async Task OpenWaitingForPlanningAsync()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await OpenViewAsync("Waiting for planning");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders waiting for planning", Level = 1 })).ToBeVisibleAsync();
    }

    // Assigns the order to Own fleet on the page waiting for planning, and leaves the outcome on the page.
    private async Task AssignToOwnFleetAsync(string number)
    {
        var select = Page.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" });
        await select.ClickAsync();
        await Expect(select).ToBeCheckedAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Own fleet" }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Transport Order {number} is planned as Shipment");
    }

    // Applied when the Planner presses Enter, which reads the first page again.
    private async Task SearchAsync(string text)
    {
        var search = Page.GetByRole(AriaRole.Textbox, new() { Name = "Search" });
        await search.FillAsync(text);
        await search.PressAsync("Enter");
    }

    private async Task ChooseAsync(string filter, string option)
    {
        await Page.GetByRole(AriaRole.Search, new() { Name = "Filter Transport Orders" }).GetByRole(AriaRole.Combobox, new() { Name = filter, Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }

    private ILocator Rows() => Page.GetByRole(AriaRole.Table, new() { Name = "Transport Orders" }).GetByRole(AriaRole.Row);

    private ILocator OrderRow(string number) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = number });

    private ILocator Footer() => Page.GetByRole(AriaRole.Navigation, new() { Name = "Pages" });

    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name });

    private ILocator Region(string name) => Page.GetByRole(AriaRole.Region, new() { Name = name, Exact = true });

    [GeneratedRegex("TR-[0-9]+")]
    private static partial Regex TripNumber();
}
