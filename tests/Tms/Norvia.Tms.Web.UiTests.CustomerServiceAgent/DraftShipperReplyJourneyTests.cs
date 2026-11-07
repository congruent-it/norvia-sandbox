using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Customer service has a reply to the Shipper drafted on a Transport Order's page, against the scripted fake model (#426, ADR 0011). Staff choose one of three questions and get an
/// editable draft in English that they edit and send themselves: the TMS sends nothing and keeps nothing. The draft is written from the Shipper's view only, so it never names the
/// Carrier or an internal Exception, even when the order has both.
/// </summary>
public sealed class DraftShipperReplyJourneyTests(CustomerServiceAgentUiFixture fixture) : TransportOrderPage(fixture)
{
    // The assistant reads the order through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    private const float AgentTimeout = 30_000;

    private const string DraftButton = "Draft a reply to the Shipper";

    [Theory]
    [InlineData("Where is my order?", "is in transit")]
    [InlineData("Has it been delivered?", "has not been delivered yet")]
    [InlineData("Is there a Proof of delivery?", "There is no Proof of delivery")]
    public async Task Draft_ForAChosenQuestion_ShowsAnEditableEnglishDraft_StaffCanEdit(string question, string expected)
    {
        var (shipmentNumber, _) = await InTransitShipmentAsync();
        var number = await TransportOrderOfAsync(shipmentNumber);
        await OpenTransportOrderAsync(number);

        await ChooseAsync(question);
        await Page.GetByRole(AriaRole.Button, new() { Name = DraftButton }).ClickAsync();

        var draft = Page.GetByRole(AriaRole.Textbox, new() { Name = "Draft reply" });
        await Expect(draft).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(draft).ToHaveValueAsync(new Regex("^Hello,"));
        await Expect(draft).ToHaveValueAsync(new Regex(Regex.Escape($"Transport Order {number}")));
        await Expect(draft).ToHaveValueAsync(new Regex(Regex.Escape(expected)));
        await Expect(draft).ToHaveValueAsync(new Regex("Kind regards,\\s+Norvia Customer service$"));
        await Expect(Page.GetByTestId("reply-draft")).ToContainTextAsync("the TMS sends nothing and keeps nothing");

        // The draft is staff's to edit.
        await draft.FillAsync("Hello,\n\nYour order is on its way, I will call you this afternoon.\n\nKind regards,\nAnna");
        await Expect(draft).ToHaveValueAsync(new Regex("I will call you this afternoon"));
    }

    [Fact]
    public async Task Draft_ForAnOrderWithACarrierAndAnInternalException_NeverNamesEither()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("DeliveryFailed", reference, DateTimeOffset.UtcNow.AddMinutes(-5));
        var exceptionNumber = await Agent.OpenExceptionOfAsync(shipmentNumber, TestContext.Current.CancellationToken);
        exceptionNumber.Should().NotBeNull("the failed delivery opens an Exception");
        var number = await TransportOrderOfAsync(shipmentNumber);
        await OpenTransportOrderAsync(number);

        foreach (var question in new[] { "Where is my order?", "Has it been delivered?", "Is there a Proof of delivery?" })
        {
            await ChooseAsync(question);
            await Page.GetByRole(AriaRole.Button, new() { Name = DraftButton }).ClickAsync();

            var draft = Page.GetByRole(AriaRole.Textbox, new() { Name = "Draft reply" });
            await Expect(draft).ToHaveValueAsync(new Regex(Regex.Escape($"Transport Order {number}")), new() { Timeout = AgentTimeout });
            foreach (var secret in new[] { "Van Dijk", "Carrier", "Exception", exceptionNumber!, shipmentNumber, "€", "EUR", "price" })
            {
                await Expect(draft).Not.ToHaveValueAsync(new Regex(Regex.Escape(secret), RegexOptions.IgnoreCase));
            }
        }
    }

    [Fact]
    public async Task Draft_SendsNothingAndKeepsNothing_TheDraftIsGoneWhenThePageIsOpenedAgain()
    {
        var (shipmentNumber, _) = await InTransitShipmentAsync();
        var number = await TransportOrderOfAsync(shipmentNumber);
        await OpenTransportOrderAsync(number);
        var outgoing = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Method != "GET" && !request.Url.Contains("/_blazor", StringComparison.Ordinal))
            {
                outgoing.Add($"{request.Method} {request.Url}");
            }
        };

        await Page.GetByRole(AriaRole.Button, new() { Name = DraftButton }).ClickAsync();
        var draft = Page.GetByRole(AriaRole.Textbox, new() { Name = "Draft reply" });
        await Expect(draft).ToBeVisibleAsync(new() { Timeout = AgentTimeout });

        // The panel has no send button and says nothing was sent.
        var panel = Page.Locator("[data-explain=shipper-reply]");
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = false })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "sent" })).ToHaveCountAsync(0);
        outgoing.Should().BeEmpty("drafting reads the Shipper's view and posts nothing");

        // Nothing is stored: the draft is not there when staff come back.
        await OpenTransportOrderAsync(number);
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Draft reply" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Draft_InThePlannerView_IsNotThere()
    {
        var number = await SubmitAcceptedAsync(Bookings.Valid($"UI-{Guid.NewGuid():N}"[..20]));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        await Page.GotoAsync($"/transport-orders/{number}");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"Transport Order {number}", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = DraftButton })).ToHaveCountAsync(0);
    }

    private async Task ChooseAsync(string question)
    {
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "The Shipper asks" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = question, Exact = true }).ClickAsync();
    }

    private async Task<string> TransportOrderOfAsync(string shipmentNumber)
    {
        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var link = Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^TO-") });
        return (await link.TextContentAsync())!.Trim();
    }
}
