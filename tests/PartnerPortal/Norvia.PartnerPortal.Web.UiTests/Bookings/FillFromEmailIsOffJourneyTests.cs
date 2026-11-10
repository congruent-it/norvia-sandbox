using Microsoft.Playwright;
using Norvia.Agents.Scripts;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.Bookings;

/// <summary>
/// The Agents are off unless the estate switches them on (<c>Agents:Enabled</c>, ADR 0040), and this AppHost leaves them off: the Partner API refuses a Booking draft, so "Fill from an email" gives the Shipper
/// "Assistant unavailable" and nothing is prefilled. The booking form works as it did. The journeys with the Agents on are in Norvia.PartnerPortal.Web.UiTests.IntakeAgent.
/// </summary>
public sealed class FillFromEmailIsOffJourneyTests(PartnerPortalUiFixture norvia) : BrowserPages(norvia)
{
    private readonly PartnerPortalUiFixture _norvia = norvia;

    [Fact]
    public async Task FillFromAnEmail_WhenTheAgentsAreOff_ShowsAssistantUnavailable_AndTheFormIsStillTheShippersToFillIn()
    {
        await Portal.GotoAsync(_norvia.PartnerPortal.ToString());
        await Portal.GetByRole(AriaRole.Form, new() { Name = "Persona" }).GetByRole(AriaRole.Button, new() { Name = "Brouwer Dranken" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Portal.GetByRole(AriaRole.Link, new() { Name = "Fill from an email" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Fill from an email", Level = 1 })).ToBeVisibleAsync();

        await Portal.GetByRole(AriaRole.Textbox, new() { Name = "The email's text" }).FillAsync(BookingIntakeSamples.Good);
        // Blazor binds the text on change, which the text area raises when it loses focus.
        await Portal.GetByRole(AriaRole.Textbox, new() { Name = "The email's text" }).BlurAsync();
        await Portal.GetByRole(AriaRole.Button, new() { Name = "Fill the booking" }).ClickAsync();

        await Expect(Portal.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable");
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Fill from an email", Level = 1 })).ToBeVisibleAsync();

        await Portal.GetByRole(AriaRole.Link, new() { Name = "Fill in the form myself" }).ClickAsync();
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Book a transport", Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByRole(AriaRole.Group, new() { Name = "Booking", Exact = true }).GetByLabel("Shipper reference", new() { Exact = true }))
            .Not.ToHaveValueAsync("SH-2030-0142");
    }
}
