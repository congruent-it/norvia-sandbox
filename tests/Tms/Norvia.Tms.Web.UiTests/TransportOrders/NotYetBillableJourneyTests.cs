using System.Net;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.TransportOrders;

/// <summary>
/// Customer service follows up a Delivered Transport Order whose Carrier never sends a Proof of delivery: it waits in Not
/// yet billable with "No Proof of delivery", Customer service releases it with a remark, and then it drops out of the list
/// without a refresh while the Shipment's timeline shows it Billable, with who released it, when and why.
/// </summary>
public sealed class NotYetBillableJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task ADeliveredOrderWithoutAProofOfDelivery_IsReleasedWithARemark_LeavesTheList_AndShowsOnTheTimelineAsBillable()
    {
        var (shipmentNumber, reference) = await InTransitShipmentAsync();
        await RecordRotterdamTrackingAsync("Delivered", reference, DateTimeOffset.UtcNow.AddMinutes(-10));

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Customer service" }).ClickAsync();
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Not yet billable" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Not yet billable", Level = 1 })).ToBeVisibleAsync();
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(row).ToContainTextAsync("No Proof of delivery");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = shipmentNumber })).ToBeVisibleAsync();

        var release = row.GetByRole(AriaRole.Button, new() { Name = "without Proof of delivery", Exact = false });
        await release.ClickAsync();
        var form = Page.GetByRole(AriaRole.Group, new() { Name = "without Proof of delivery", Exact = false });
        await form.GetByRole(AriaRole.Button, new() { Name = "Release without Proof of delivery" }).ClickAsync();
        await Expect(form).ToContainTextAsync("Give a remark");

        await form.GetByRole(AriaRole.Textbox, new() { Name = "Remark" }).FillAsync("CMR received by post, scanned in the dossier");
        await form.GetByRole(AriaRole.Button, new() { Name = "Release without Proof of delivery" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "released without a Proof of delivery" })).ToBeVisibleAsync();
        await Expect(row).ToHaveCountAsync(0);

        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var billing = Page.GetByRole(AriaRole.Region, new() { Name = "Billing" });
        await Expect(billing).ToContainTextAsync("Billable");
        await Expect(billing).ToContainTextAsync("Released without Proof of delivery");
        await Expect(billing).ToContainTextAsync("Sanne Bakker");
        await Expect(billing).ToContainTextAsync("CMR received by post, scanned in the dossier");
    }
}
