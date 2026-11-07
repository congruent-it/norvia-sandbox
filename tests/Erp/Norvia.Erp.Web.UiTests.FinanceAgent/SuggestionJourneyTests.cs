using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// Finance acts on a Suggestion of the Carrier invoice matching agent, against the scripted fake model (ADR 0041). Each journey records its own Carrier costs and Carrier invoice, opens a line that is
/// To decide in Finance's "Carrier invoices to decide" and chooses Explain: the Suggestion pre-selects the action and executes nothing, Finance writes the remark or reason and confirms with the button
/// as ever, and a line that cannot be accepted is never offered Accept. Every invoice has a second line that stays To decide, so its section stays on the page after the first line is decided.
/// </summary>
public sealed class SuggestionJourneyTests(FinanceAgentUiFixture fixture) : ToDecidePage(fixture)
{
    private static ILocator Suggested(ILocator invoice) => invoice.GetByTestId("suggested-decision");

    private static ILocator AcceptButton(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Button, new() { Name = $"Accept {reference}" });

    private static ILocator RejectButton(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Button, new() { Name = $"Reject {reference}" });

    /// <summary>Receives an invoice of the line and of a second one that is To decide and stays so.</summary>
    private async Task<string> ReceiveWithCompanionAsync(params (string Reference, decimal Amount)[] lines)
    {
        var companion = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(companion, 10m);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, [.. lines, (companion, 15m)]);
        return invoiceNumber;
    }

    [Fact]
    public async Task Suggestion_ToAcceptAdjusted_PreSelectsItAndDecidesNothing_UntilFinanceWritesTheRemarkAndConfirms()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordRateCardCostAsync(reference, 80m);
        var invoiceNumber = await ReceiveWithCompanionAsync((reference, 95m));
        var invoice = await OpenLineAsync(invoiceNumber, reference);
        await Expect(Suggested(invoice)).ToHaveCountAsync(0);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(ExplanationOf(invoice, reference).GetByTestId("action-kind")).ToHaveTextAsync("Accept adjusted", new() { Timeout = AgentTimeout });
        await Expect(Suggested(invoice)).ToContainTextAsync("The assistant suggests: Accept adjusted. Nothing is decided until you confirm; enter the adjusted amount and a remark.");

        // It pre-selected and executed nothing: the line is still To decide.
        await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
        await invoice.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex($"^Adjusted amount for {Regex.Escape(reference)} ") }).FillAsync("80.00");
        await invoice.GetByRole(AriaRole.Textbox, new() { Name = $"Remark on the adjusted amount for {reference}" }).FillAsync("Agreed Rate Card price.");
        await AcceptButton(invoice, reference).ClickAsync();

        var line = invoice.GetByLabel($"Line {reference}");
        await Expect(line).ToContainTextAsync("Accepted", new() { Timeout = AgentTimeout });
        await Expect(line).ToContainTextAsync("Agreed Rate Card price.");
    }

    [Fact]
    public async Task Suggestion_ToReject_PreSelectsItAndDecidesNothing_UntilFinanceWritesTheReasonAndConfirms()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(reference, 100m);
        var earlier = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(earlier, (reference, 100m));
        var invoiceNumber = await ReceiveWithCompanionAsync((reference, 100m));
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(ExplanationOf(invoice, reference).GetByTestId("action-kind")).ToHaveTextAsync("Reject", new() { Timeout = AgentTimeout });
        await Expect(Suggested(invoice)).ToContainTextAsync("The assistant suggests: Reject. Nothing is decided until you confirm.");
        await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
        await invoice.GetByRole(AriaRole.Textbox, new() { Name = $"Reason for rejecting {reference}" }).FillAsync($"Duplicates Carrier invoice {earlier}.");
        await RejectButton(invoice, reference).ClickAsync();

        var line = invoice.GetByLabel($"Line {reference}");
        await Expect(line).ToContainTextAsync("Rejected", new() { Timeout = AgentTimeout });
        await Expect(line).ToContainTextAsync($"Duplicates Carrier invoice {earlier}.");
    }

    [Fact]
    public async Task Suggestion_OnAnUnknownBookingLine_IsToWait_PreSelectsNothing_AndNeverOffersAccept()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var invoiceNumber = await ReceiveWithCompanionAsync((reference, 70m));
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        // Wait or Reject, whichever the ERP allows, and never Accept: a late Carrier cost matches the line by itself.
        await Expect(ExplanationOf(invoice, reference).GetByTestId("action-kind")).ToHaveTextAsync("Wait", new() { Timeout = AgentTimeout });
        await Expect(Suggested(invoice)).ToHaveCountAsync(0);
        await Expect(AcceptButton(invoice, reference)).ToHaveCountAsync(0);
        await Expect(RejectButton(invoice, reference)).ToBeVisibleAsync();
        await Expect(invoice.GetByLabel($"Line {reference}")).ToContainTextAsync("To decide");
    }

    [Fact]
    public async Task Suggestion_OnADuplicateLine_IsToReject_AndNeverOffersAccept()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(reference, 100m);
        await ReceiveInvoiceAsync(CarrierInvoiceCalls.NewInvoiceNumber(), (reference, 100m));
        var invoiceNumber = await ReceiveWithCompanionAsync((reference, 100m));
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(ExplanationOf(invoice, reference).GetByTestId("action-kind")).ToHaveTextAsync("Reject", new() { Timeout = AgentTimeout });
        await Expect(AcceptButton(invoice, reference)).ToHaveCountAsync(0);
        await Expect(RejectButton(invoice, reference)).ToBeVisibleAsync();
    }
}
