using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>
/// An operator runs a plan whose Carriers deliver and prove every delivery and whose Finance invoices the Shipper and credits part of a line (#601): the run log
/// shows the invoice and the Credit note Finance issued in the ERP, and both as they arrive in the Shipper Persona's invoices inbox. The edge cases (no Credit note
/// share, an unreachable ERP or inbox, the same seed twice) are in the integration tests.
/// </summary>
public sealed partial class InvoicingJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 6;

    // The deliveries go through the TMS, the Billing Logic App's stand-in and the ERP before Finance can invoice, and the documents through the Shipper invoice Logic App's stand-in to the inbox.
    private const float PatienceMilliseconds = 300_000;

    [Fact]
    public async Task AnOperatorRunsAPlanWithInvoicing_AndSeesTheInvoiceAndTheCreditNote_AsTheyArriveInTheShippersInbox()
    {
        // A seed of this test's own, so its plan and its Shipper references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every booking is good, planned and released, every Carrier confirms, tracks, delivers and proves the delivery without an Exception, so each Transport Order becomes
        // Billable, and Finance invoices every Shipper and credits every invoice in part.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 0), ("Reassigned (%)", 0),
            ("Released by the Planner (%)", 100), ("Confirmed by the Carrier (%)", 100), ("Declined by the Carrier (%)", 0), ("Tracked (%)", 100),
            ("Exceptions on the way (%)", 0), ("Delivered (%)", 100), ("With a Proof of delivery (%)", 100), ("Failed collections cancelled (%)", 0),
            ("Own fleet put on Trips (%)", 0), ("Shippers invoiced (%)", 100), ("Invoices credited in part (%)", 100),
        })
        {
            await Page.GetByLabel(label).FillAsync(percent.ToString(CultureInfo.InvariantCulture));
        }

        await Page.GetByLabel("Seed").FillAsync(seed.ToString(CultureInfo.InvariantCulture));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview plan" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Plan preview");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Run ");
        await Expect(Page.GetByRole(AriaRole.Status).First).ToContainTextAsync("Completed", new() { Timeout = PatienceMilliseconds });

        var log = Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" });
        var rows = log.GetByRole(AriaRole.Row);

        // Invoice now reports the invoice number, with a Message trail from the ERP's answer.
        var invoice = rows.Filter(new() { Has = Page.Locator("[data-outcome=InvoiceIssued]") });
        await Expect(invoice).ToHaveCountAsync(1);
        await Expect(invoice).ToContainTextAsync("Finance");
        await Expect(invoice).ToContainTextAsync("Invoice now");
        await Expect(invoice).ToContainTextAsync("without VAT");
        await Expect(invoice.GetByRole(AriaRole.Link, new() { NameRegex = TrailLinks.Name() })).ToHaveCountAsync(1);
        var invoiceNumber = InvoiceNumber().Match(await invoice.InnerTextAsync()).Value;
        invoiceNumber.Should().NotBeEmpty("Invoice now reports the invoice number");

        // The partial Credit note is on a line of that invoice, with its reason, and credits part of the line.
        var creditNote = rows.Filter(new() { Has = Page.Locator("[data-outcome=CreditNoteIssued]") });
        await Expect(creditNote).ToHaveCountAsync(1);
        await Expect(creditNote).ToContainTextAsync($"on {invoiceNumber}");
        await Expect(creditNote).ToContainTextAsync("Simulated Rate Card correction");
        var creditNoteText = await creditNote.InnerTextAsync();
        var creditNoteNumber = CreditNoteNumber().Match(creditNoteText).Value;
        creditNoteNumber.Should().NotBeEmpty();
        var amounts = CreditedAmounts().Match(creditNoteText);
        amounts.Success.Should().BeTrue(creditNoteText);
        decimal.Parse(amounts.Groups["credited"].Value, CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0).And.BeLessThan(decimal.Parse(amounts.Groups["total"].Value, CultureInfo.InvariantCulture), "a partial Credit note credits part of the line");

        // Both documents arrive in the Shipper Persona's invoices inbox, which is not Finance's.
        var received = rows.Filter(new() { Has = Page.Locator("[data-outcome=InboxReceived]") });
        await Expect(received).ToHaveCountAsync(2);
        await Expect(received.Filter(new() { HasText = invoiceNumber })).ToHaveCountAsync(1);
        await Expect(received.Filter(new() { HasText = creditNoteNumber })).ToHaveCountAsync(1);
        await Expect(received.Filter(new() { HasText = "Finance" })).ToHaveCountAsync(0);

        // The log is read from the database, so a refresh shows the same documents: the invoice, the Credit note on it and the invoice in the inbox.
        await Page.ReloadAsync();

        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }).GetByRole(AriaRole.Row)
            .Filter(new() { HasText = invoiceNumber })).ToHaveCountAsync(3, new() { Timeout = 30_000 });
    }

    [GeneratedRegex(@"INV-\d{4}-\d{6}")]
    private static partial Regex InvoiceNumber();

    [GeneratedRegex(@"CN-\d{4}-\d{6}")]
    private static partial Regex CreditNoteNumber();

    [GeneratedRegex(@"EUR (?<credited>\d+\.\d{2}) of EUR (?<total>\d+\.\d{2}) without VAT")]
    private static partial Regex CreditedAmounts();
}
