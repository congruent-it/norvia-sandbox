using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.CarrierInvoices;

/// <summary>
/// The Agents are off unless the estate switches them on (<c>Agents:Enabled</c>, ADR 0040), and this AppHost sets them off explicitly: Finance's "Carrier invoices to decide" works as it did, with a line
/// that is To decide, and offers no Explain. The journeys with the Agents on are in Norvia.Erp.Web.UiTests.FinanceAgent.
/// </summary>
public sealed class ExplainIsOffJourneyTests(ErpUiFixture erp) : BrowserPage(erp)
{
    [Fact]
    public async Task ALineThatIsToDecide_HasNoExplainButton_WhenTheAgentsAreOff()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        using (var costs = Erp.LogicAppClient(CarrierInvoiceCalls.RecordCarrierCostRole))
        {
            await CarrierInvoiceCalls.RecordCarrierCostAsync(costs, reference, 80m, Ct);
        }

        using (var invoices = Erp.LogicAppClient(CarrierInvoiceCalls.ReceiveCarrierInvoiceRole))
        {
            await CarrierInvoiceCalls.ReceiveCarrierInvoiceAsync(invoices, invoiceNumber, Ct, (reference, 95m));
        }

        await Page.GotoAsync("/carrier-invoices/to-decide");
        var invoice = Page.GetByRole(AriaRole.Region, new() { NameRegex = new Regex($"^Carrier invoice {Regex.Escape(invoiceNumber)} ") });
        await invoice.GetByRole(AriaRole.Button, new() { NameRegex = new Regex($"^{Regex.Escape(reference)} ") }).ClickAsync();

        // The line is open and Finance can decide it, but nothing offers to explain it.
        await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
        await Expect(invoice.GetByRole(AriaRole.Button, new() { Name = "Explain" })).ToHaveCountAsync(0);
    }
}
