using Microsoft.Playwright;
using Norvia.PartnerPortal.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.TrackingAssistant;

/// <summary>The steps every journey shares: enter a Transport Order for the Shipper, play its Persona and open the order's tracking page.</summary>
public abstract class BrowserPage(TrackingAssistantUiFixture norvia) : RecordedBrowserPage(norvia.Browser)
{
    // The Shipper Persona that asks and the Shipper it plays, from the seed: Brouwer Dranken is Shipper SHP-0001.
    private static readonly (string Persona, string ShipperId) Asking = ("Brouwer Dranken", "SHP-0001");

    protected TrackingAssistantUiFixture Norvia { get; } = norvia;

    /// <summary>The conversation's answers (<c>assistant-answer</c>) and notices (<c>assistant-notice</c>), in the order they were shown.</summary>
    protected ILocator Conversation() => Portal.GetByRole(AriaRole.List, new() { Name = "Conversation" });

    protected ILocator Answers() => Conversation().GetByTestId("assistant-answer");

    protected ILocator Notices() => Conversation().GetByTestId("assistant-notice");

    /// <summary>Enters a Transport Order for the Shipper, plays its Persona and opens the order's tracking page. Returns the order's number.</summary>
    protected async Task<string> OpenOrderAsync(Uri portal)
    {
        var reference = NewReference();
        var number = await Norvia.Tms.EnterTransportOrderAsync(Asking.ShipperId, reference, Ct);

        await Portal.GotoAsync(portal.ToString());
        await Portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = Asking.Persona }).ClickAsync();
        await Portal.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Tracking" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Tracking", Level = 1 })).ToBeVisibleAsync();
        await Portal.GetByLabel("Transport Order number or Shipper reference", new() { Exact = true }).FillAsync(reference);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Ask about your orders", Level = 2 })).ToBeVisibleAsync();
        return number;
    }

    /// <summary>Types the question into the panel and asks it.</summary>
    protected async Task AskAsync(string question)
    {
        await Portal.GetByLabel("Your question", new() { Exact = true }).FillAsync(question);
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Ask", Exact = true }).ClickAsync();
    }

    /// <summary>The order's tracking page is still on screen, with its Tracking status: the assistant does not take it away.</summary>
    protected async Task ExpectOrderStillTrackedAsync(string number)
    {
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByLabel("Transport Order", new() { Exact = true })).ToContainTextAsync("Received");
    }

    // Unique per test run and short, as Norvia keeps each Shipper reference once per Shipper.
    private static string NewReference() => $"UI-{Guid.NewGuid():N}"[..20];
}
