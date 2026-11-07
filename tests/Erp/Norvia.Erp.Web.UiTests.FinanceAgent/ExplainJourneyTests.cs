using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// Finance asks the Carrier invoice matching agent to explain a line that is To decide, against the scripted fake model (ADR 0041). Each journey records its own Carrier costs and
/// Carrier invoice, opens the line in Finance's "Carrier invoices to decide" and chooses Explain: the answer gives the cause in plain language and the evidence it cites, and
/// changes nothing; the line stays To decide until Finance decides it.
/// </summary>
public sealed class ExplainJourneyTests(FinanceAgentUiFixture fixture) : ToDecidePage(fixture)
{
    [Fact]
    public async Task Explain_OnALineWithAMangledReference_NamesALikelyCandidateNeverACertainty_AndCitesTheTool()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var booking = await RecordCostAsync(reference, 100m);
        var mangled = reference.Replace("-", string.Empty, StringComparison.Ordinal);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (mangled, 100m));
        var invoice = await OpenLineAsync(invoiceNumber, mangled);

        await ExplainButton(invoice, mangled).ClickAsync();

        var explanation = ExplanationOf(invoice, mangled);
        await Expect(explanation).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync($"No Carrier cost has arrived under the reference {mangled}");
        await Expect(explanation).ToContainTextAsync($"{booking} (reference {reference}, €100.00) is a likely candidate, but that is not certain");
        await Expect(explanation).ToContainTextAsync("The line is matched again by itself when a Carrier cost for this reference arrives");
        await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"The best likely candidate is Carrier cost {booking}" })).ToContainTextAsync("get_invoice_line");
        await Expect(explanation).ToContainTextAsync($"Check with the Carrier whether the line is about Carrier cost {booking}");
        await Expect(explanation).ToContainTextAsync("Confidence: medium. The assistant can be wrong; you decide.");
    }

    [Fact]
    public async Task Explain_OnAPriceMismatch_GivesTheDifferenceAndTheTolerance_AndCitesTheTool()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(reference, 80m);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (reference, 95m));
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        var explanation = ExplanationOf(invoice, reference);
        await Expect(explanation).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync("The Carrier claims €95.00, €15.00 more than the agreed Carrier cost of €80.00 (Spot). The tolerance is €0.00, so the line did not match.");
        await Expect(explanation).ToContainTextAsync("It stays To decide until Finance decides it; it does not resolve by itself.");
        await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "The agreed Carrier cost is €80.00, from Spot." })).ToContainTextAsync("get_invoice_line");
        await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "The matching tolerance is €0.00." })).ToContainTextAsync("get_invoice_line");
        await Expect(explanation).ToContainTextAsync("Ask the Planner what was agreed for this Spot price before you decide.");

        // The assistant changes nothing: the line is still To decide, and Finance still decides it.
        await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
    }

    [Fact]
    public async Task Explain_OnADuplicate_NamesTheEarlierInvoiceThatInvoicedTheCarrierCost_AndCitesTheTool()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(reference, 100m);
        var earlier = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(earlier, (reference, 100m));
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (reference, 100m));
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        var explanation = ExplanationOf(invoice, reference);
        await Expect(explanation).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync($"The Carrier cost for {reference} is already Invoiced: line {reference} of Carrier invoice {earlier}");
        await Expect(explanation).ToContainTextAsync("This line claims it a second time. It does not resolve by itself.");
        await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"The line is a Duplicate of line {reference} of Carrier invoice {earlier}." })).ToContainTextAsync("get_invoice_line");
        await Expect(explanation).ToContainTextAsync($"Reject the line with the reason that it duplicates Carrier invoice {earlier}");
    }

    // The whole ERP UI is Finance's, and a user without the Finance role is refused every page (the sign-in is Easy Auth's in Azure, a configured local user here), so there is no
    // other view to open. What decides where Explain shows is the line: it is on a line that is To decide and on no other, which this journey shows beside a line that matched.
    [Fact]
    public async Task Explain_IsNotOnALineThatMatched_OnlyOnTheOneThatIsToDecide()
    {
        var matched = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(matched, 100m);
        var toDecide = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(toDecide, 80m);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (matched, 100m), (toDecide, 95m));

        var invoice = await OpenLineAsync(invoiceNumber, toDecide);
        await invoice.GetByRole(AriaRole.Button, new() { NameRegex = new Regex($"^{Regex.Escape(matched)} ") }).ClickAsync();

        await Expect(invoice.GetByLabel($"Line {matched}")).ToContainTextAsync("Matched");
        await Expect(ExplainButton(invoice, toDecide)).ToBeVisibleAsync();
        await Expect(ExplainButton(invoice, matched)).ToHaveCountAsync(0);
    }
}
