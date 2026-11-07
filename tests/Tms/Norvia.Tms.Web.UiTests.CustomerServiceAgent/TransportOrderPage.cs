using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>A browser page on the Transport Order detail page, as Customer service opens it.</summary>
public abstract class TransportOrderPage(CustomerServiceAgentUiFixture agent) : BrowserPage(agent)
{
    protected CustomerServiceAgentUiFixture Agent { get; } = agent;

    protected const string ExplainButton = "Explain this Transport Order";

    // The page reads the role from the request while it prerenders, so Customer service opens it with a full page load, as the list's links do not.
    protected async Task OpenTransportOrderAsync(string number)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();
        await Page.GotoAsync($"/transport-orders/{number}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton })).ToBeVisibleAsync();
    }
}
