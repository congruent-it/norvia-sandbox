using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Simulator.Web.UiTests.Runs;

/// <summary>
/// An operator runs a plan whose Carriers deliver and invoice what they delivered, with every invoice carrying one edited amount, and whose Finance accepts what it can
/// at the agreed amount (#632): the run log shows each invoice and its lines as Norvia answered them, Finance's decision with its remark, and the outcome as it arrives on
/// the Carrier Persona's invoices page. The edge cases (rejections, an unreachable ERP or inbox, no delivery) are in the integration tests.
/// </summary>
public sealed class CarrierInvoicingJourneyTests(SimulatorUiFixture simulator) : BrowserPage(simulator)
{
    private const int Orders = 6;

    // The deliveries go through the TMS, the Billing Logic App's stand-in and the ERP before a Carrier can invoice them, and the outcomes through the Carrier invoice
    // outcome Logic App's stand-in to the Carrier Persona's invoices page.
    private const float PatienceMilliseconds = 300_000;

    [Fact]
    public async Task AnOperatorRunsAPlanWithCarrierInvoicing_AndSeesTheInvoice_TheDecision_AndTheOutcome_AsTheyArriveOnTheCarriersInvoicesPage()
    {
        // A seed of this test's own, so its plan and its Carrier references are its own.
        var seed = Random.Shared.Next(1_000, int.MaxValue);
        TestContext.Current.SendDiagnosticMessage($"Seed {seed}");
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Configure a run");

        // Every booking is good, planned and released, every Carrier confirms, tracks, delivers and proves the delivery, so each delivering Carrier invoices. Every invoice
        // carries one edited amount, and Finance rejects nothing it can accept at the agreed amount. Shippers are not invoiced, which this journey doesn't look at.
        await Page.GetByLabel("Orders").FillAsync(Orders.ToString(CultureInfo.InvariantCulture));
        foreach (var (label, percent) in new[]
        {
            ("Bad bookings (%)", 0), ("Assigned by the Planner (%)", 100), ("To Own fleet where it fits (%)", 0), ("Reassigned (%)", 0),
            ("Released by the Planner (%)", 100), ("Confirmed by the Carrier (%)", 100), ("Declined by the Carrier (%)", 0), ("Tracked (%)", 100),
            ("Exceptions on the way (%)", 0), ("Delivered (%)", 100), ("With a Proof of delivery (%)", 100), ("Failed collections cancelled (%)", 0),
            ("Own fleet put on Trips (%)", 0), ("Shippers invoiced (%)", 0), ("Invoices credited in part (%)", 0),
            ("Carrier invoices with an edited amount (%)", 100), ("Carrier invoice lines rejected (%)", 0),
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

        // Each delivering Carrier Persona's invoice is in the log with its number and the result of each line as Norvia answered it, and a Message trail.
        var sent = rows.Filter(new() { Has = Page.Locator("[data-outcome=CarrierInvoiced]") });
        await Expect(sent.First).ToBeVisibleAsync();
        await Expect(sent.First).ToContainTextAsync("Send Carrier invoice");
        await Expect(sent.First).ToContainTextAsync("-SIM");
        await Expect(sent.First.GetByRole(AriaRole.Link, new() { NameRegex = TrailLinks.Name() })).ToHaveCountAsync(1);
        await Expect(sent.Filter(new() { HasText = "PRICE_MISMATCH" }).First).ToBeVisibleAsync();

        // Finance accepts the edited line at the agreed amount, with a remark.
        var accepted = rows.Filter(new() { Has = Page.Locator("[data-outcome=LineAccepted]") });
        await Expect(accepted.First).ToBeVisibleAsync();
        await Expect(accepted.First).ToContainTextAsync("Finance");
        await Expect(accepted.First).ToContainTextAsync("accepted at the agreed");
        await Expect(accepted.First).ToContainTextAsync("Agreed price applies");

        // The outcome arrives on the Carrier Persona's invoices page as adjusted, with Finance's remark, which is not Finance's own line.
        var outcome = rows.Filter(new() { Has = Page.Locator("[data-outcome=OutcomeReceived]") }).Filter(new() { HasText = "ACCEPTED_ADJUSTED" });
        await Expect(outcome.First).ToBeVisibleAsync();
        await Expect(outcome.First).ToContainTextAsync("Agreed price applies");
        await Expect(outcome.First).Not.ToContainTextAsync("Finance");

        // The log is read from the database, so a refresh shows the same lines.
        await Page.ReloadAsync();

        await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Log lines" }).GetByRole(AriaRole.Row)
            .Filter(new() { Has = Page.Locator("[data-outcome=OutcomeReceived]") }).Filter(new() { HasText = "ACCEPTED_ADJUSTED" }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
