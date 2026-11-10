using System.Net.Http.Json;
using System.Text.Json.Nodes;
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

    protected ILocator RowOf(string shipperReference) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });

    protected static string NewReference() => $"UI-{Guid.NewGuid():N}"[..20];

    /// <summary>A booking collected on the day, at ten past eight in Norvia's time zone.</summary>
    protected static JsonObject BookingOn(string shipperReference, DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(8, 10));
        return Bookings.Valid(shipperReference).CollectedFrom(new DateTimeOffset(local, Amsterdam.GetUtcOffset(local)));
    }

    /// <summary>The first weekday with room for the order and no warning: not Over capacity, and with spare weight for it and a margin.</summary>
    protected async Task<DateOnly> DayThatFitsAsync()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Amsterdam).DateTime);
        var days = await OpenDaysAsync();
        return days
            .Where(day => day.Date > today && day.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Where(day => day.Load is { } load && !load["over"]!.GetValue<bool>() && load["capacityKg"]!.GetValue<decimal>() - load["demandKg"]!.GetValue<decimal>() > 2_000m)
            .Select(day => day.Date)
            .First();
    }

    /// <summary>The day the demo seed leaves Over capacity, which the Planner sees flagged on the Capacity view.</summary>
    protected async Task<DateOnly> OverCapacityDayAsync() =>
        (await OpenDaysAsync()).First(day => day.Load is { } load && load["over"]!.GetValue<bool>()).Date;

    private async Task<IReadOnlyList<(DateOnly Date, JsonNode? Load)>> OpenDaysAsync()
    {
        using var response = await Agent.Manage.GetAsync(new Uri("/v1/capacity/days", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var days = await response.Content.ReadFromJsonAsync<JsonArray>(TestContext.Current.CancellationToken);
        return [.. days!.Select(day => (DateOnly.Parse(day!["date"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture), day["load"]))];
    }
}
