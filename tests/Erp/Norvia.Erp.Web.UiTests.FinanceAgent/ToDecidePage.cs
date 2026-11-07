using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// A fresh browser page per test, on Finance's "Carrier invoices to decide" (<see cref="TracedPage"/> keeps the trace and video of a failed test). The tests share the ERP and set up
/// their own data under numbers of their own, so none empties it.
/// </summary>
public abstract class ToDecidePage(FinanceAgentUiFixture fixture) : TracedPage(fixture.Browser, fixture.ErpUi)
{
    // The assistant reads the line through the MCP stand-in and the model before it answers; the first call on a cold host takes longer than a page does.
    protected const float AgentTimeout = 30_000;

    protected FinanceAgentUiFixture Agent { get; } = fixture;

    /// <summary>Records a Spot Carrier cost as the Carrier cost Logic App does.</summary>
    protected async Task<string> RecordCostAsync(string reference, decimal total)
    {
        using var client = Agent.LogicAppClient(CarrierInvoiceCalls.RecordCarrierCostRole);
        return await CarrierInvoiceCalls.RecordCarrierCostAsync(client, reference, total, Ct);
    }

    /// <summary>Receives a Carrier invoice as the Carrier invoice Logic App does.</summary>
    protected async Task ReceiveInvoiceAsync(string invoiceNumber, params (string Reference, decimal Amount)[] lines)
    {
        using var client = Agent.LogicAppClient(CarrierInvoiceCalls.ReceiveCarrierInvoiceRole);
        await CarrierInvoiceCalls.ReceiveCarrierInvoiceAsync(client, invoiceNumber, Ct, lines);
    }

    /// <summary>Opens Finance's "Carrier invoices to decide" and the line of an invoice, which is closed until Finance opens it.</summary>
    protected async Task<ILocator> OpenLineAsync(string invoiceNumber, string reference)
    {
        await Page.GotoAsync("/carrier-invoices/to-decide");
        var invoice = Page.GetByRole(AriaRole.Region, new() { NameRegex = new Regex($"^Carrier invoice {Regex.Escape(invoiceNumber)} ") });
        await Expect(invoice).ToBeVisibleAsync();
        await invoice.GetByRole(AriaRole.Button, new() { NameRegex = new Regex($"^{Regex.Escape(reference)} ") }).ClickAsync();
        return invoice;
    }

    /// <summary>The Explain button of a line, in the invoice's section.</summary>
    protected static ILocator ExplainButton(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Button, new() { Name = $"Explain {reference}" });

    /// <summary>The explanation of a line, once the assistant has answered.</summary>
    protected static ILocator ExplanationOf(ILocator invoice, string reference) => invoice.GetByTestId($"explanation-{reference}");
}
