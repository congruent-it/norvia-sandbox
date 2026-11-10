using Microsoft.Playwright;
using Norvia.Agents.Scripts;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.IntakeAgent.Bookings;

/// <summary>
/// An Agent that fails costs the Shipper nothing but the shortcut (ADR 0040): the portal says "Assistant unavailable" and the booking form is still theirs to fill in. This takes the Partner API away, so
/// it is its own class and the tests of the fixture run one at a time (xunit.runner.json).
/// </summary>
public sealed class AssistantUnavailableJourneyTests(IntakeAgentUiFixture norvia) : BrowserPage(norvia)
{
    [Fact]
    public async Task WhenTheAssistantFails_ThePortalShowsAssistantUnavailable_AndTheShipperCanStillFillInTheForm()
    {
        await OpenFillFromEmailAsync();
        await PasteEmailAsync(BookingIntakeSamples.Good);

        await Norvia.StopPartnerApiAsync(Ct);
        try
        {
            await Portal.GetByRole(AriaRole.Button, new() { Name = "Fill the booking" }).ClickAsync();

            // The portal waits 90 seconds for the Partner API (PartnerApi:Timeout) before it gives up.
            await Expect(Portal.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable", new() { Timeout = PartnerApiGivesUp });
            // Nothing else changes: the text is kept, the page can ask again and the Shipper can go on by hand.
            await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Fill from an email", Level = 1 })).ToBeVisibleAsync();
            await Expect(Portal.GetByRole(AriaRole.Textbox, new() { Name = "The email's text" })).ToHaveValueAsync(BookingIntakeSamples.Good);
            await Expect(Portal.GetByRole(AriaRole.Button, new() { Name = "Fill the booking" })).ToBeEnabledAsync();
            await Expect(Portal.GetByRole(AriaRole.Link, new() { Name = "Fill in the form myself" })).ToBeVisibleAsync();
        }
        finally
        {
            await Norvia.StartPartnerApiAsync(Ct);
        }
    }
}
