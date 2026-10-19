using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.MasterData;

/// <summary>
/// Finance checks what the invoices are based on in the master data lists the ERP loaded from the seed file: Shippers, Carriers, Rate Cards and
/// Carrier Rate Cards. They are read-only: no list offers an action.
/// </summary>
public sealed class MasterDataJourneyTests(ErpUiFixture erp) : BrowserPage(erp)
{
    [Fact]
    public async Task TheMasterDataLists_ShowTheSeedLoadedShippersCarriersAndRateCards_ReadOnly()
    {
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Shippers", Level = 1 })).ToBeVisibleAsync();
        var shipper = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "SHP-0001" });
        await Expect(shipper).ToContainTextAsync(BrouwerName);
        await Expect(shipper).ToContainTextAsync("Yes");
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "SHP-0002" })).ToContainTextAsync("No");

        await OpenViewAsync("Carriers");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Carriers", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "CAR-0001" })).ToContainTextAsync("Van Dijk Transport");

        await OpenViewAsync("Rate Cards");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Rate Cards", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("RC-0001").First).ToBeVisibleAsync();

        await OpenViewAsync("Carrier Rate Cards");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Carrier Rate Cards", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("CRC-0001").First).ToBeVisibleAsync();

        await Expect(Page.GetByRole(AriaRole.Main).GetByRole(AriaRole.Button)).ToHaveCountAsync(0);
    }

    private Task OpenViewAsync(string title) => OpenViewAsync("Master data", title);
}
