using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.Invoicing;

/// <summary>
/// Finance watches the Waiting billable lines while the Billing Logic App hands lines over: Brouwer Dranken's row appears and grows without a
/// refresh. Finance chooses Invoice now for Brouwer Dranken, the row leaves the list, and the invoice Finance is told about is in the Shipper
/// invoices and in Brouwer Dranken's invoices inbox in the Partner portal.
/// </summary>
public sealed class InvoiceNowJourneyTests(ErpUiFixture erp) : BrowserPage(erp)
{
    [Fact]
    public async Task WaitingLinesOfAShipper_AreInvoicedWithInvoiceNow_LeaveTheList_AndShowAsAnInvoiceThatReachesTheShipper()
    {
        await Page.GotoAsync("/billable-lines");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Waiting billable lines", Level = 1 })).ToBeVisibleAsync();

        // The lines arrive while the list is open.
        await RecordLineAsync(Brouwer, freight: 100m);
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = BrouwerName });
        await Expect(row).ToContainTextAsync("€ 100.00");
        await RecordLineAsync(Brouwer, freight: 150m);
        await Expect(row).ToContainTextAsync("€ 250.00");

        await row.GetByRole(AriaRole.Button, new() { Name = $"Invoice now for {BrouwerName}" }).ClickAsync();

        var outcome = row.GetByRole(AriaRole.Status).Filter(new() { HasText = "Issued INV-" });
        await Expect(outcome).ToContainTextAsync("over 2 lines");
        var invoiceNumber = (await outcome.GetByRole(AriaRole.Link).InnerTextAsync()).Trim();
        invoiceNumber.Should().MatchRegex(@"^INV-\d{4}-\d{6}$");
        await Expect(row).ToHaveCountAsync(0);

        await OpenViewAsync("Billing", "Shipper invoices");
        var invoice = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = invoiceNumber });
        await Expect(invoice).ToContainTextAsync(BrouwerName);
        await Expect(invoice).ToContainTextAsync("€ 250.00");
        await Expect(invoice).ToContainTextAsync("€ 52.50");
        await Expect(invoice).ToContainTextAsync("€ 302.50");

        // The Shipper invoice Logic App's report arrives without a refresh too.
        await Expect(invoice.GetByRole(AriaRole.Gridcell, new() { Name = "Sent", Exact = true })).ToBeVisibleAsync();

        await Page.GotoAsync(new Uri(Erp.PartnerPortal, "/personas/brouwer-dranken/invoices").ToString());
        await Expect(Page.GetByRole(AriaRole.Article, new() { Name = invoiceNumber })).ToContainTextAsync("€ 250.00");
    }
}
