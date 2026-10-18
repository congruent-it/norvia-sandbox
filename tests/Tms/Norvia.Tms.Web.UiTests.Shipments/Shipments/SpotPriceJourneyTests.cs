using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Tms.Web.UiTests.Shipments;

/// <summary>
/// Van Dijk Transport's Carrier Rate Card has a deliberate gap: no Lane into Germany. Releasing a Shipment to Düsseldorf
/// to Van Dijk fails in Shipments to release with the pricing failure's code, and the Planner releases that Shipment with
/// a Spot price. The Carrier cost and its source then show in the bookings awaiting an answer and on the Shipment.
/// </summary>
public sealed class SpotPriceJourneyTests(TmsUiFixture tms) : BrowserPage(tms)
{
    [Fact]
    public async Task AShipmentTheCarrierRateCardCantPrice_ShowsItsCode_AndReleasesWithASpotPriceTheBookingsShow()
    {
        var shipperReference = $"UI-{Guid.NewGuid():N}"[..20];
        var number = await SubmitAcceptedAsync(Bookings.Valid(shipperReference).DeliveredToDusseldorf());

        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Planner" }).ClickAsync();
        var order = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipperReference });
        await order.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {number}" }).ClickAsync();
        var assign = Page.GetByRole(AriaRole.Group, new() { Name = "Assign" });
        await assign.GetByRole(AriaRole.Combobox, new() { Name = "Assign to" }).ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Van Dijk Transport" }).ClickAsync();
        await assign.GetByRole(AriaRole.Button, new() { Name = "Assign selected" }).ClickAsync();
        var planned = Page.GetByRole(AriaRole.List, new() { Name = "Assignment outcomes" }).GetByRole(AriaRole.Status);
        await Expect(planned).ToContainTextAsync($"Transport Order {number} is planned as Shipment");
        var shipmentNumber = await planned.GetByRole(AriaRole.Link).TextContentAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Shipments to release" }).ClickAsync();
        var shipmentRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });

        // The TMS says it can't price the Shipment before any release is tried (in plain words, as the UI words the code), and says so again after a reload.
        await Expect(shipmentRow).ToContainTextAsync("The Carrier Rate Card has no Lane for this origin and destination.");
        await Page.ReloadAsync();
        await Expect(shipmentRow).ToContainTextAsync("The Carrier Rate Card has no Lane for this origin and destination.");
        await shipmentRow.GetByRole(AriaRole.Checkbox, new() { Name = $"Select {shipmentNumber}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Group, new() { Name = "Release" }).GetByRole(AriaRole.Button, new() { Name = "Release selected" }).ClickAsync();

        var failure = Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Alert);
        await Expect(failure).ToContainTextAsync($"Shipment {shipmentNumber} wasn't released (NoMatchingLane)");
        await Expect(failure).ToContainTextAsync("Van Dijk Transport");
        await Expect(shipmentRow).ToContainTextAsync("The Carrier Rate Card has no Lane for this origin and destination.");

        await shipmentRow.GetByRole(AriaRole.Button, new() { Name = $"Release {shipmentNumber} with Spot price" }).ClickAsync();
        var form = Page.GetByRole(AriaRole.Group, new() { Name = $"Release {shipmentNumber} with Spot price" });
        await form.GetByRole(AriaRole.Button, new() { Name = "Release with Spot price" }).ClickAsync();
        await Expect(form).ToContainTextAsync("Give an amount in EUR");
        await form.GetByRole(AriaRole.Textbox, new() { Name = "Amount (EUR, without VAT)" }).FillAsync("480.50");
        await form.GetByRole(AriaRole.Textbox, new() { Name = "Remark" }).FillAsync("agreed with Van Dijk by phone, €480.50");
        await form.GetByRole(AriaRole.Button, new() { Name = "Release with Spot price" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.List, new() { Name = "Release outcomes" }).GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Shipment {shipmentNumber} is released.");
        await Expect(shipmentRow).ToHaveCountAsync(0);

        await Page.GetByRole(AriaRole.Link, new() { Name = "Bookings awaiting an answer" }).ClickAsync();
        var awaiting = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = shipmentNumber });
        await Expect(awaiting).ToContainTextAsync("480.50 EUR · Spot price: agreed with Van Dijk by phone, €480.50");

        await Page.GotoAsync($"/shipments/{shipmentNumber}");
        var booking = Page.GetByRole(AriaRole.Table, new() { Name = "Carrier bookings" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "CB-" });
        await Expect(booking).ToContainTextAsync("480.50 EUR · Spot price: agreed with Van Dijk by phone, €480.50");
        var timeline = Page.GetByRole(AriaRole.Table, new() { Name = "Carrier cost by Carrier booking" }).GetByRole(AriaRole.Row).Filter(new() { HasText = "CB-" });
        await Expect(timeline).ToContainTextAsync("480.50 EUR · Spot price: agreed with Van Dijk by phone, €480.50");
    }
}
