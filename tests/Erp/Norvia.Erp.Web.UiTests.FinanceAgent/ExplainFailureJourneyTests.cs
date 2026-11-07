using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// An Agent that fails costs the page nothing but the explanation (ADR 0040). It stops the MCP stand-in, which Aspire may fail to stop once <see cref="ExplainJourneyTests"/> has made
/// its tool calls through it, so <see cref="StopsTheAgentFirst"/> runs this class before that one.
/// </summary>
public sealed class ExplainFailureJourneyTests(FinanceAgentUiFixture fixture) : ToDecidePage(fixture)
{
    [Fact]
    public async Task Explain_WhenTheAgentFails_ShowsAssistantUnavailableAndFinanceCanStillDecide()
    {
        var (invoiceNumber, reference) = await ReceivePriceMismatchAsync();
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await Agent.StopAgentAsync(Ct);
        try
        {
            await ExplainButton(invoice, reference).ClickAsync();

            await Expect(invoice.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable", new() { Timeout = AgentTimeout });
            await Expect(ExplanationOf(invoice, reference)).ToHaveCountAsync(0);

            // Nothing else changes: the line can be asked about again and is still Finance's to decide.
            await Expect(ExplainButton(invoice, reference)).ToBeEnabledAsync();
            await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
        }
        finally
        {
            await Agent.StartAgentAsync(Ct);
        }
    }

    [Fact]
    public async Task ExplainInvoice_WhenTheAgentFails_ShowsAssistantUnavailableAndTheLinesAreStillFinancesToDecide()
    {
        var (invoiceNumber, reference) = await ReceivePriceMismatchAsync();
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await Agent.StopAgentAsync(Ct);
        try
        {
            await ExplainInvoiceButton(invoice, invoiceNumber).ClickAsync();

            await Expect(invoice.GetByRole(AriaRole.Alert)).ToContainTextAsync("Assistant unavailable", new() { Timeout = AgentTimeout });
            await Expect(InvoiceExplanationOf(invoice, invoiceNumber)).ToHaveCountAsync(0);
            await Expect(ExplainInvoiceButton(invoice, invoiceNumber)).ToBeEnabledAsync();
            await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
        }
        finally
        {
            await Agent.StartAgentAsync(Ct);
        }
    }
}
