using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.PlannerAgentPlan;

/// <summary>
/// A browser page on the Planner's list of Transport Orders waiting for planning, and the orders a Plan the list journey submits for itself. The AppHost is this project's own, so the list holds only
/// those orders; <see cref="SubmitAsync"/> hands them back so the test can take them off the list when it is done, and the next test finds the list as empty as the first did.
/// </summary>
public abstract class PlanTheListPage(PlannerAgentPlanUiFixture fixture) : BrowserPage(fixture), IAsyncLifetime
{
    protected const float PlanTimeout = 120_000;

    private readonly List<string> _submitted = [];

    protected ILocator AssignGroup => Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });

    protected ILocator Outcomes => Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" });

    protected ILocator RowOf(Order order) => Page.GetByRole(AriaRole.Row).Filter(new() { HasText = order.Reference });

    protected ILocator SuggestionFor(Order order) =>
        Page.GetByTestId("plan-suggestion").Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = order.Number }) });

    protected async Task<Order> SubmitAsync()
    {
        var reference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(reference));
        _submitted.Add(number);
        return new Order(reference, number);
    }

    // The role is read from the request while the list prerenders, so the role view is chosen first and the list opened with a full page load.
    protected async Task OpenListAsync(Order lastSubmitted)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Page.GotoAsync("/transport-orders");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Transport Orders waiting for planning", Level = 1 })).ToBeVisibleAsync();
        await Expect(RowOf(lastSubmitted)).ToBeVisibleAsync();
    }

    protected async Task PlanTheListAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Plan the list" }).ClickAsync();
        await Expect(Page.GetByTestId("plan")).ToBeVisibleAsync(new() { Timeout = PlanTimeout });
    }

    // The base class's lifetime is not virtual, so the page names the interface again and wraps it.
    public new ValueTask InitializeAsync() => base.InitializeAsync();

    /// <summary>Plans every order the test submitted to Own fleet, as another Planner would, so none is left waiting for the next test.</summary>
    public new async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        foreach (var number in _submitted)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/transport-orders/{number}/assign", UriKind.Relative))
            {
                Content = JsonContent.Create(new JsonObject { ["assignedTo"] = "OwnFleet" }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var response = await fixture.Manage.SendAsync(request, CancellationToken.None);
            response.EnsureSuccessStatusCode();
        }

        await base.DisposeAsync();
    }

    protected sealed record Order(string Reference, string Number);
}
