using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.Invoicing;

/// <summary>
/// The Shipper invoice Logic App is down when Finance issues an invoice, and the Shipper's endpoint fails: the invoice shows Not delivered
/// without a refresh, Finance chooses Send again once the Logic App is back, and the invoice shows Sent and reaches the Shipper's invoices inbox.
/// </summary>
public sealed class SendAgainJourneyTests(ErpUiFixture erp) : BrowserPage(erp)
{
    [Fact]
    public async Task ANotDeliveredInvoice_IsSentAgain_AndShowsAsSentInTheListAndTheShippersInbox()
    {
        string invoiceNumber;
        await Erp.StopShipperInvoiceWorkerAsync(Ct);
        try
        {
            await RecordLineAsync(Brouwer, freight: 100m);
            await Page.GotoAsync("/billable-lines");
            await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = BrouwerName })
                .GetByRole(AriaRole.Button, new() { Name = $"Invoice now for {BrouwerName}" }).ClickAsync();
            var issued = Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Issued INV-" }).GetByRole(AriaRole.Link);
            invoiceNumber = (await issued.InnerTextAsync()).Trim();

            await OpenViewAsync("Billing", "Shipper invoices");
            var invoice = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = invoiceNumber });
            await Expect(invoice.GetByRole(AriaRole.Button, new() { Name = $"Send {invoiceNumber} again" })).ToHaveCountAsync(0);

            await Erp.ReportInvoiceNotDeliveredAsync(invoiceNumber, Ct);
            await Expect(invoice).ToContainTextAsync("Not delivered");
        }
        finally
        {
            await Erp.StartShipperInvoiceWorkerAsync(Ct);
        }

        var notDelivered = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Not delivered" });
        await notDelivered.GetByRole(AriaRole.Button, new() { Name = "again", Exact = false }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Sent again" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Not delivered" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Gridcell, new() { Name = "Sent", Exact = true })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(Erp.PartnerPortal, "/personas/brouwer-dranken/invoices").ToString());
        await Expect(Page.GetByRole(AriaRole.Article, new() { Name = invoiceNumber })).ToHaveCountAsync(1);
    }
}
