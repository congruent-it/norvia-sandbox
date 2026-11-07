using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Customer service asks the assistant to explain a Transport Order, against the scripted fake model. The answer tells the order's life so far with the evidence it cites and what is
/// blocking it; it shows a loading state while the assistant reads and says what it could not tell for an order with no Shipment yet. Its Agent-fails journey is in
/// <see cref="ExplainTransportOrderFailureJourneyTests"/>.
/// </summary>
public sealed class ExplainTransportOrderJourneyTests(CustomerServiceAgentUiFixture fixture) : TransportOrderPage(fixture)
{
    // The assistant reads the order through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    private const float AgentTimeout = 30_000;

    [Fact]
    public async Task Explain_OnADeliveredOrderWithoutAProofOfDelivery_ShowsItsLifeCitedWithWhatIsBlockingIt()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-10));
        var number = await TransportOrderOfAsync(shipmentNumber);

        await OpenTransportOrderAsync(number);
        await Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton }).ClickAsync();

        var explanation = Page.GetByTestId("explanation");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Its life so far" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync($"Transport Order {number} is");
        await Expect(explanation).ToContainTextAsync($"Shipment {shipmentNumber} is delivered.");
        await Expect(explanation).ToContainTextAsync("the last is delivered at Rotterdam");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Evidence" })).ToBeVisibleAsync();
        await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "The last of 3 Tracking events is Delivered at Rotterdam" })).ToHaveCountAsync(1);
        await Expect(explanation.Locator("cite").Filter(new() { HasText = "get_transport_order" }).First).ToBeVisibleAsync();
        await Expect(explanation.Locator("cite").Filter(new() { HasText = "get_shipment_timeline" })).ToHaveCountAsync(1);
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "What is blocking it" })).ToBeVisibleAsync();
        await Expect(explanation).ToContainTextAsync("It has no Proof of delivery.");
        await Expect(explanation).ToContainTextAsync("you decide");
    }

    [Fact]
    public async Task Explain_WhileTheAssistantReads_ShowsItIsReadingAndThenTheExplanation()
    {
        var number = await SubmitAcceptedAsync(Bookings.Valid($"UI-{Guid.NewGuid():N}"[..20]));
        await OpenTransportOrderAsync(number);
        // Records whether the "reading" status was ever on the page, so the check doesn't race the answer.
        await Page.EvaluateAsync(
            """
            () => {
                window.__sawReading = false;
                new MutationObserver(() => {
                    for (const status of document.querySelectorAll('[role=status]')) {
                        if (status.textContent.includes('The assistant is reading')) { window.__sawReading = true; }
                    }
                }).observe(document, { subtree: true, childList: true, characterData: true });
            }
            """);

        await Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton }).ClickAsync();

        await Expect(Page.GetByTestId("explanation")).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        (await Page.EvaluateAsync<bool>("() => window.__sawReading")).Should().BeTrue();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "The assistant is reading" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton })).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Explain_OnAnOrderWithNoShipmentYet_SaysItHasNotBeenPlanned()
    {
        var number = await SubmitAcceptedAsync(Bookings.Valid($"UI-{Guid.NewGuid():N}"[..20]));

        await OpenTransportOrderAsync(number);
        await Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton }).ClickAsync();

        var explanation = Page.GetByTestId("explanation");
        await Expect(explanation.GetByRole(AriaRole.Heading, new() { Name = "Its life so far" })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync("It has no Shipment yet, so it has not been planned.");
        await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "The Transport Order has no Shipment." })).ToHaveCountAsync(1);
        await Expect(explanation.Locator("cite").Filter(new() { HasText = "get_transport_order" }).First).ToBeVisibleAsync();
        await Expect(explanation.Locator("cite").Filter(new() { HasText = "get_shipment_timeline" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Explain_InThePlannerView_IsNotThere()
    {
        var number = await SubmitAcceptedAsync(Bookings.Valid($"UI-{Guid.NewGuid():N}"[..20]));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Page.GotoAsync($"/transport-orders/{number}");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = ExplainButton })).ToHaveCountAsync(0);
    }

    private async Task<string> TransportOrderOfAsync(string shipmentNumber)
    {
        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var link = Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^TO-") });
        return (await link.TextContentAsync())!.Trim();
    }
}
