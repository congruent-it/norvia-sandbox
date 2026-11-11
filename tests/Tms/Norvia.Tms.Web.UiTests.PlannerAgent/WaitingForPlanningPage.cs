using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgent;

/// <summary>
/// A browser page on the list of Transport Orders waiting for planning, as the Planner opens it, and the data the Planner agent's journeys set up for themselves: an order collected on a day
/// Own fleet can carry it, or on the demo seed's Over capacity day. Nothing here assigns an order, so no test changes the load another test reads.
/// </summary>
public abstract class WaitingForPlanningPage(PlannerAgentUiFixture fixture) : BrowserPage(fixture)
{
    // The assistant reads the options through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    protected const float AgentTimeout = 30_000;

    // The order weighs 910.5 kg; the margin keeps it clear of a day that is nearly full.
    private const decimal SpareKgForTheOrder = 2_000m;

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    protected PlannerAgentUiFixture Agent { get; } = fixture;

    // The list reads the role from the request while it prerenders, so the role view is chosen first and the list opened with a full page load.
    protected async Task OpenListAsync(string role, string shipperReference)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = role }).ClickAsync();
        await Page.GotoAsync("/transport-orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders waiting for planning", Level = 1 })).ToBeVisibleAsync();
        await Expect(RowOf(shipperReference)).ToBeVisibleAsync();
    }

    protected ILocator AssignGroup => Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });

    protected ILocator Outcomes => Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" });

    /// <summary>Asks the assistant about the row and waits for its Suggestion, which the page has acted on by the time it shows.</summary>
    protected static async Task<ILocator> SuggestAsync(ILocator row, string number)
    {
        await row.GetByRole(AriaRole.Button, new() { Name = $"Suggest for {number}" }).ClickAsync();
        var suggestion = row.GetByTestId("suggestion");
        await Expect(suggestion.GetByRole(AriaRole.Heading, new() { Name = "Suggestion" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        return suggestion;
    }

    /// <summary>The Carrier a Suggestion names ("Assign to Carrier Kessler Spedition at 120.00 EUR").</summary>
    protected static async Task<string> SuggestedCarrierAsync(ILocator suggestion)
    {
        var action = await suggestion.GetByTestId("suggested-action").InnerTextAsync();
        return Regex.Match(action, @"Assign to Carrier (?<name>.+?) at \d+\.\d{2} EUR").Groups["name"].Value;
    }

    /// <summary>Plans the order as another Planner would, behind this page's back, so the page still lists it.</summary>
    protected async Task AssignBehindThePageAsync(string number, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/transport-orders/{number}/assign", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["assignedTo"] = "OwnFleet" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await Agent.Manage.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    protected ILocator RowOf(string shipperReference) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });

    protected static string NewReference() => $"UI-{Guid.NewGuid():N}"[..20];

    /// <summary>A booking collected on the day, at ten past eight in Norvia's time zone.</summary>
    protected static JsonObject BookingOn(string shipperReference, DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(8, 10));
        return Bookings.Valid(shipperReference).CollectedFrom(new DateTimeOffset(local, Amsterdam.GetUtcOffset(local)));
    }

    /// <summary>The first weekday with room for the order and no warning: not Over capacity, and with spare weight for it and a margin.</summary>
    protected async Task<DateOnly> DayThatFitsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amsterdam).DateTime);
        var days = await OpenDaysAsync(cancellationToken);
        var fitting = days.FirstOrDefault(day => day.Date > today && day.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && day.HasRoomFor(SpareKgForTheOrder));
        return fitting?.Date ?? throw new InvalidOperationException($"No open weekday after {today} has {SpareKgForTheOrder} kg to spare and is not Over capacity.");
    }

    /// <summary>The day the demo seed leaves Over capacity, which the Planner sees flagged on the Capacity view.</summary>
    protected async Task<DateOnly> OverCapacityDayAsync(CancellationToken cancellationToken) =>
        (await OpenDaysAsync(cancellationToken)).FirstOrDefault(day => day.Over)?.Date
        ?? throw new InvalidOperationException("The demo seed left no open day Over capacity.");

    private async Task<IReadOnlyList<CapacityDay>> OpenDaysAsync(CancellationToken cancellationToken)
    {
        using var response = await Agent.Manage.GetAsync(new Uri("/v1/capacity/days", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var days = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
        return [.. days!.Select(day => CapacityDay.Of(day!))];
    }

    /// <summary>One open day of the Planning horizon and its load check, as the Capacity view reads it.</summary>
    private sealed record CapacityDay(DateOnly Date, bool Over, decimal SpareKg, bool HasLoad)
    {
        public bool HasRoomFor(decimal kg) => HasLoad && !Over && SpareKg > kg;

        public static CapacityDay Of(JsonNode day)
        {
            var date = DateOnly.Parse(day["date"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
            return day["load"] is { } load
                ? new CapacityDay(date, load["over"]!.GetValue<bool>(), load["capacityKg"]!.GetValue<decimal>() - load["demandKg"]!.GetValue<decimal>(), true)
                : new CapacityDay(date, false, 0, false);
        }
    }
}
