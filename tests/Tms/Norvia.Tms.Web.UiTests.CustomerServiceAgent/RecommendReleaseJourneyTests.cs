using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Tms.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.CustomerServiceAgent;

/// <summary>
/// Customer service asks the assistant whether a row of the not-yet-billable list may be released without a Proof of delivery, against the scripted fake model. A Release suggestion only
/// pre-selects the order in the release flow: staff write the remark and confirm. An order the TMS says is not eligible (here: not rated) shows the blocking reasons and no Release.
/// The text never drafts the remark.
/// </summary>
public sealed class RecommendReleaseJourneyTests(CustomerServiceAgentUiFixture agent) : BrowserPage(agent)
{
    // The assistant reads the order through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    private const float AgentTimeout = 30_000;
    private const float AttemptTimeout = 8_000;
    private const int MaxAttempts = 3;

    // The TMS calls a Proof of delivery missing 48 hours after the Carrier reported the Delivery.
    private static readonly TimeSpan LongAgo = TimeSpan.FromDays(4);

    [Fact]
    public async Task Recommend_OnAnEligibleOrder_SuggestsReleaseAndOnlyPreSelectsIt_UntilStaffWriteTheRemarkAndConfirm()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync(startedAgo: LongAgo);
        await RecordRotterdamTrackingAsync("Delivered", reference, DateTimeOffset.UtcNow.AddDays(-3));
        var row = await OpenNotYetBillableRowAsync(shipmentNumber);
        await Expect(row).ToContainTextAsync("No Proof of delivery");
        var number = await OrderNumberAsync(row);

        var recommendation = await RecommendAsync(row, number);
        await Expect(recommendation.GetByTestId("action-kind")).ToHaveTextAsync("Release", new() { Timeout = AgentTimeout });
        await Expect(recommendation.GetByRole(AriaRole.Heading, new() { Name = "Evidence" })).ToBeVisibleAsync();
        await Expect(recommendation.Locator("cite").First).ToContainTextAsync("get_release_eligibility");
        await Expect(recommendation).ToContainTextAsync("You write the remark yourself and confirm the release");
        await Expect(recommendation).ToContainTextAsync("you decide, and you write the remark");

        // The text drafts no remark, and the suggestion released nothing: no form is open and the order is still listed with no Proof of delivery.
        await Expect(recommendation).Not.ToContainTextAsync("CMR");
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "without Proof of delivery", Exact = false })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "released without a Proof of delivery" })).ToHaveCountAsync(0);
        await Expect(row).ToContainTextAsync("No Proof of delivery");

        // Reviewing only pre-selects the order: the form is open for it, the remark is empty, and nothing is released yet.
        await recommendation.GetByRole(AriaRole.Button, new() { Name = $"Review release of {number}" }).ClickAsync();
        var form = Page.GetByRole(AriaRole.Group, new() { Name = $"Release {number} without Proof of delivery" });
        await Expect(form).ToBeVisibleAsync();
        var remark = form.GetByRole(AriaRole.Textbox, new() { Name = "Remark" });
        await Expect(remark).ToHaveValueAsync("");
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "released without a Proof of delivery" })).ToHaveCountAsync(0);
        await Expect(row).ToContainTextAsync("No Proof of delivery");

        // Staff write the remark and confirm; the release then runs as any other.
        await remark.FillAsync("CMR received by post, scanned in the dossier");
        await form.GetByRole(AriaRole.Button, new() { Name = "Release without Proof of delivery" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = $"Transport Order {number} is released without a Proof of delivery" })).ToBeVisibleAsync();
        await Expect(row).ToHaveCountAsync(0);
        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var billing = Page.GetByRole(AriaRole.Region, new() { Name = "Billing" });
        await Expect(billing).ToContainTextAsync("Released without Proof of delivery");
        await Expect(billing).ToContainTextAsync("CMR received by post, scanned in the dossier");
    }

    [Fact]
    public async Task Recommend_OnAnOrderThatIsNotEligible_ShowsTheBlockingReasonsAndNoRelease()
    {
        // A Shipper without a Rate Card: the order is never rated, and releasing it would not make it billable. (A booking to Düsseldorf would fail too, but the Carrier's Rate Card has
        // no Lane there either, so the Planner could not release it to the Carrier without a Spot price.)
        var (shipmentNumber, reference) = await InTransitShipmentAsync(booking => { booking["shipperId"] = "SHP-0003"; return booking; }, LongAgo);
        await RecordRotterdamTrackingAsync("Delivered", reference, DateTimeOffset.UtcNow.AddDays(-3));
        var row = await OpenNotYetBillableRowAsync(shipmentNumber);
        await Expect(row).ToContainTextAsync("Not rated");
        await Expect(row).ToContainTextAsync("No Proof of delivery");
        var number = await OrderNumberAsync(row);

        var recommendation = await RecommendAsync(row, number);
        await Expect(recommendation.GetByTestId("action-kind")).ToHaveTextAsync("Needs a person", new() { Timeout = AgentTimeout });
        await Expect(recommendation).ToContainTextAsync($"Transport Order {number} cannot be released without a Proof of delivery now: it is not rated");
        await Expect(recommendation.GetByRole(AriaRole.Listitem)).ToContainTextAsync(["The TMS lists NotRated as blocking the release."]);
        await Expect(recommendation.Locator("cite")).ToContainTextAsync("get_release_eligibility");
        await Expect(recommendation).ToContainTextAsync("Do not release it");
        await Expect(recommendation).ToContainTextAsync("open its rating exception first");

        // No Release is suggested: there is nothing to review, and nothing was released.
        await Expect(recommendation.GetByRole(AriaRole.Button, new() { Name = "Review release", Exact = false })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "without Proof of delivery", Exact = false })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "released without a Proof of delivery" })).ToHaveCountAsync(0);
        await Expect(row).ToContainTextAsync("Not rated");
    }

    // The list reads the role from the request while it prerenders, so Customer service opens it with a full page load.
    private async Task<ILocator> OpenNotYetBillableRowAsync(string shipmentNumber)
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();
        await Page.GotoAsync("/not-yet-billable");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Not yet billable", Level = 1 })).ToBeVisibleAsync();
        return Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
    }

    // The list reads itself again every second and a row it redraws starts over, so an answer that was still on its way when that happened never shows. Ask again then, as staff would.
    private static async Task<ILocator> RecommendAsync(ILocator row, string number)
    {
        var recommendation = row.GetByTestId("recommendation");
        for (var attempt = 1; ; attempt++)
        {
            await row.GetByRole(AriaRole.Button, new() { Name = $"Recommend for {number}" }).ClickAsync();
            try
            {
                await Expect(recommendation).ToBeVisibleAsync(new() { Timeout = attempt < MaxAttempts ? AttemptTimeout : AgentTimeout });
                return recommendation;
            }
            catch (PlaywrightException) when (attempt < MaxAttempts)
            {
            }
        }
    }

    private static async Task<string> OrderNumberAsync(ILocator row) =>
        (await row.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("^TO-") }).TextContentAsync())!.Trim();
}
