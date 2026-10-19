using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.Invoicing;

/// <summary>
/// Finance corrects a wrong charge on one invoice line with a partial Credit note: it gets a number and a reason, it lists on the invoice
/// without a refresh, and a second Credit note for more than what is left on the line is refused with the reason said beside the form.
/// </summary>
public sealed class CreditNoteJourneyTests(ErpUiFixture erp) : BrowserPage(erp)
{
    [Fact]
    public async Task APartialCreditNote_IsIssuedOnAnInvoiceLine_ListsOnTheInvoice_AndCreditingMoreThanIsLeftIsRefused()
    {
        var transportOrderNumber = await RecordLineAsync(Brouwer, freight: 100m);
        var invoiceNumber = await InvoiceBrouwerAsync();

        await Page.GotoAsync($"/shipper-invoices/{invoiceNumber}");
        await Page.GetByRole(AriaRole.Button, new() { Name = transportOrderNumber, Exact = false }).ClickAsync();

        await Page.GetByLabel($"Amount to credit on {transportOrderNumber}").FillAsync("40.00");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = $"Reason for the Credit note on {transportOrderNumber}" }).FillAsync("Pallet damaged on delivery");
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Issue Credit note on {transportOrderNumber}" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Issued Credit note CN-" })).ToBeVisibleAsync();
        var creditNotes = Page.GetByLabel($"Credit notes on {invoiceNumber}");
        var creditNote = creditNotes.GetByRole(AriaRole.Row).Filter(new() { HasText = "Pallet damaged on delivery" });
        await Expect(creditNote).ToContainTextAsync("€ 40.00");
        await Expect(creditNote).ToContainTextAsync("€ 8.40");
        await Expect(creditNote).ToContainTextAsync("€ 48.40");
        await Expect(creditNote.GetByRole(AriaRole.Link, new() { Name = invoiceNumber })).ToBeVisibleAsync();

        // 60 euros are left on the line, so 70 is too much.
        await Page.GetByLabel($"Amount to credit on {transportOrderNumber}").FillAsync("70.00");
        await Page.GetByRole(AriaRole.Textbox, new() { Name = $"Reason for the Credit note on {transportOrderNumber}" }).FillAsync("Second claim");
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Issue Credit note on {transportOrderNumber}" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "60" })).ToBeVisibleAsync();
        await Expect(creditNotes.GetByRole(AriaRole.Row).Filter(new() { HasText = "Second claim" })).ToHaveCountAsync(0);
    }

    private async Task<string> InvoiceBrouwerAsync()
    {
        await Page.GotoAsync("/billable-lines");
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = BrouwerName })
            .GetByRole(AriaRole.Button, new() { Name = $"Invoice now for {BrouwerName}" }).ClickAsync();
        var issued = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Issued INV-" }).GetByRole(AriaRole.Link);
        return (await issued.InnerTextAsync()).Trim();
    }
}
