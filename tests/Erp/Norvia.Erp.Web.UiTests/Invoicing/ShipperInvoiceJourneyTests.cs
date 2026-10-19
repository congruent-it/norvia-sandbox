using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.Invoicing;

/// <summary>
/// Finance opens a Shipper invoice from the list of Shipper invoices and sees what the Shipper was charged: the totals with VAT, the invoice's
/// status once the Shipper's endpoint acknowledged it and, per line, the Shipper reference and the charge lines.
/// </summary>
public sealed class ShipperInvoiceJourneyTests(ErpUiFixture erp) : BrowserPage(erp)
{
    [Fact]
    public async Task AShipperInvoice_IsOpenedFromTheList_AndShowsItsTotalsStatusAndLines()
    {
        var transportOrderNumber = await RecordLineAsync(Brouwer, freight: 100m, minimumChargeAdjustment: 20m);

        await Page.GotoAsync("/billable-lines");
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = BrouwerName })
            .GetByRole(AriaRole.Button, new() { Name = $"Invoice now for {BrouwerName}" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Issued INV-" })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Shipper invoices" }).ClickAsync();
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = BrouwerName }).GetByRole(AriaRole.Link, new() { NameRegex = new(@"^INV-\d{4}-\d{6}$") }).ClickAsync();

        var details = Page.GetByLabel("Shipper invoice INV-");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Shipper invoice INV-", Exact = false, Level = 1 })).ToBeVisibleAsync();
        await Expect(details).ToContainTextAsync($"{BrouwerName} (SHP-0001)");
        await Expect(details).ToContainTextAsync("€ 120.00");
        await Expect(details).ToContainTextAsync("€ 25.20");
        await Expect(details).ToContainTextAsync("€ 145.20");
        await Expect(details).ToContainTextAsync("Sent");

        await Page.GetByRole(AriaRole.Button, new() { Name = transportOrderNumber, Exact = false }).ClickAsync();
        var line = Page.GetByLabel($"Invoice line {transportOrderNumber}");
        await Expect(line).ToContainTextAsync($"REF-{transportOrderNumber}");
        var charges = Page.GetByLabel($"Charge lines of {transportOrderNumber}");
        await Expect(charges).ToContainTextAsync("€ 100.00");
        await Expect(charges).ToContainTextAsync("€ 20.00");
    }
}
