using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// Finance asks the Carrier invoice matching agent to explain a whole Carrier invoice, against the scripted fake model (ADR 0041). Each journey records its own Carrier costs and Carrier invoice, and chooses
/// "Explain this invoice" on it in Finance's "Carrier invoices to decide": the answer summarises the invoice, groups the lines To decide by cause with the counts and says which may still resolve by themselves.
/// It changes nothing. The failure of the Agent is in <see cref="ExplainFailureJourneyTests"/>.
/// </summary>
public sealed class ExplainInvoiceJourneyTests(FinanceAgentUiFixture fixture) : ToDecidePage(fixture)
{
    [Fact]
    public async Task ExplainInvoice_OnAnInvoiceWithAMixOfResults_SummarisesItAndGroupsTheLinesToDecideByCause_AndCitesTheTool()
    {
        var matched = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(matched, 100m);
        var mismatch = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(mismatch, 80m);
        var duplicate = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(duplicate, 60m);
        var unknown = CarrierInvoiceCalls.NewReference();
        var earlier = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(earlier, (duplicate, 60m));
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (matched, 100m), (mismatch, 95m), (duplicate, 60m), (unknown, 70m));
        var invoice = await OpenInvoiceAsync(invoiceNumber);

        await ExplainInvoiceButton(invoice, invoiceNumber).ClickAsync();

        var explanation = InvoiceExplanationOf(invoice, invoiceNumber);
        await Expect(explanation).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(explanation).ToContainTextAsync($"Carrier invoice {invoiceNumber} of Van Dijk Transport claims €325.00 on 4 lines");
        await Expect(explanation).ToContainTextAsync("3 lines are To decide: 1 Price mismatch, 1 Unknown booking, 1 Duplicate.");
        foreach (var cause in new[] { "Price mismatch", "Unknown booking", "Duplicate" })
        {
            await Expect(explanation.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"1 line is To decide as {cause}." })).ToContainTextAsync("get_invoice_summary");
        }

        await Expect(explanation).ToContainTextAsync("1 of them may still resolve by themselves, each matched again when its Carrier cost arrives (only the Unknown booking lines can). The other 2 wait for Finance.");
        await Expect(explanation).ToContainTextAsync("Confidence: high. The assistant can be wrong; you decide.");

        // No Spot remark reaches the answer.
        await Expect(explanation).Not.ToContainTextAsync("Agreed by phone");
    }

    [Fact]
    public async Task ExplainInvoice_ChangesNothing_TheLinesAreStillToDecideAndFinanceCanAskAgain()
    {
        var (invoiceNumber, reference) = await ReceivePriceMismatchAsync();
        var invoice = await OpenInvoiceAsync(invoiceNumber);

        await ExplainInvoiceButton(invoice, invoiceNumber).ClickAsync();

        await Expect(InvoiceExplanationOf(invoice, invoiceNumber)).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await LineButton(invoice, reference).ClickAsync();
        await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
        await Expect(ExplainInvoiceButton(invoice, invoiceNumber)).ToBeEnabledAsync();
    }

    [Fact]
    public async Task ExplainInvoice_WhileTheAssistantReads_ShowsItIsReading_AndThenTheAnswer()
    {
        var (invoiceNumber, reference) = await ReceivePriceMismatchAsync();
        var invoice = await OpenInvoiceAsync(invoiceNumber);

        // The fake answers in milliseconds, so the page records that it showed the reading state rather than the test racing it.
        await Page.EvaluateAsync(
            """
            window.__sawReading = false;
            new MutationObserver(() => {
                if (document.body.innerText.includes('The assistant is reading')) { window.__sawReading = true; }
            }).observe(document.body, { childList: true, subtree: true, characterData: true });
            """);
        await ExplainInvoiceButton(invoice, invoiceNumber).ClickAsync();

        await Expect(InvoiceExplanationOf(invoice, invoiceNumber)).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        (await Page.EvaluateAsync<bool>("window.__sawReading")).Should().BeTrue();
        await Expect(invoice.GetByRole(AriaRole.Status)).ToHaveCountAsync(0);
    }
}
