using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// Finance acts on a Suggestion of the Carrier invoice matching agent, against the scripted fake model (ADR 0041). Each journey records its own Carrier costs and Carrier invoice, opens a line that is
/// To decide in Finance's "Carrier invoices to decide" and chooses Explain: the Suggestion pre-selects the action and executes nothing, Finance writes the remark or reason and confirms with the button
/// as ever, and a line that cannot be accepted is never offered Accept. Every invoice has a second line that stays To decide, so its section stays on the page after the first line is decided.
/// The fake suggests Accept adjusted only for a Carrier cost priced from a Carrier Rate Card, and never a plain Accept, so that is the Accept the journeys use.
/// </summary>
public sealed class SuggestionJourneyTests(FinanceAgentUiFixture fixture) : ToDecidePage(fixture)
{
    // The second line of every invoice: a Spot cost of 10.00 claimed at 15.00, a Price mismatch that nobody decides.
    private const decimal SecondLineCost = 10m;
    private const decimal SecondLineClaim = 15m;

    private static ILocator Suggested(ILocator invoice) => invoice.GetByTestId("suggested-decision");

    private static ILocator SuggestedKind(ILocator invoice, string reference) => ExplanationOf(invoice, reference).GetByTestId("action-kind");

    private static ILocator LineOf(ILocator invoice, string reference) => invoice.GetByLabel($"Line {reference}");

    private static ILocator AcceptButton(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Button, new() { Name = $"Accept {reference}" });

    private static ILocator RejectButton(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Button, new() { Name = $"Reject {reference}" });

    /// <summary>Receives an invoice of the line and of a second one that is To decide and stays so.</summary>
    private async Task<string> ReceiveWithSecondLineAsync(string reference, decimal claim)
    {
        var second = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(second, SecondLineCost);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (reference, claim), (second, SecondLineClaim));
        return invoiceNumber;
    }

    /// <summary>An invoice whose line duplicates one an earlier invoice claimed; returns the invoice and the earlier invoice's number.</summary>
    private async Task<(string InvoiceNumber, string Earlier)> ReceiveDuplicateAsync(string reference)
    {
        await RecordCostAsync(reference, 100m);
        var earlier = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(earlier, (reference, 100m));
        return (await ReceiveWithSecondLineAsync(reference, 100m), earlier);
    }

    [Fact]
    public async Task Suggestion_ToAcceptAdjusted_PreSelectsItOnTheForm_AndDecidesNothing()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordRateCardCostAsync(reference, 80m);
        var invoiceNumber = await ReceiveWithSecondLineAsync(reference, 95m);
        var invoice = await OpenLineAsync(invoiceNumber, reference);
        await Expect(Suggested(invoice)).ToHaveCountAsync(0);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(SuggestedKind(invoice, reference)).ToHaveTextAsync("Accept adjusted", new() { Timeout = AgentTimeout });
        await Expect(Suggested(invoice)).ToContainTextAsync("The assistant suggests: Accept adjusted. Nothing is decided until you confirm; enter the adjusted amount and a remark.");

        // Nothing was decided, not now and not after the ERP has been read again: the line is still To decide, and the Suggestion was for the session only.
        await Expect(LineOf(invoice, reference)).ToContainTextAsync("To decide");
        var reopened = await OpenLineAsync(invoiceNumber, reference);
        await Expect(LineOf(reopened, reference)).ToContainTextAsync("To decide");
        await Expect(Suggested(reopened)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Suggestion_ToAcceptAdjusted_IsDecidedWhenFinanceWritesTheRemarkAndConfirms()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordRateCardCostAsync(reference, 80m);
        var invoiceNumber = await ReceiveWithSecondLineAsync(reference, 95m);
        var invoice = await OpenLineAsync(invoiceNumber, reference);
        await ExplainButton(invoice, reference).ClickAsync();
        await Expect(Suggested(invoice)).ToBeVisibleAsync(new() { Timeout = AgentTimeout });

        await invoice.GetByRole(AriaRole.Textbox, new() { NameRegex = new Regex($"^Adjusted amount for {Regex.Escape(reference)} ") }).FillAsync("80.00");
        await invoice.GetByRole(AriaRole.Textbox, new() { Name = $"Remark on the adjusted amount for {reference}" }).FillAsync("Agreed Rate Card price.");
        await AcceptButton(invoice, reference).ClickAsync();

        var line = LineOf(invoice, reference);
        await Expect(line.GetByText("Accepted", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(line).ToContainTextAsync("Agreed Rate Card price.");
    }

    [Fact]
    public async Task Suggestion_ToReject_PreSelectsItOnTheForm_AndDecidesNothing()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var (invoiceNumber, _) = await ReceiveDuplicateAsync(reference);
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(SuggestedKind(invoice, reference)).ToHaveTextAsync("Reject", new() { Timeout = AgentTimeout });
        await Expect(Suggested(invoice)).ToContainTextAsync("The assistant suggests: Reject. Nothing is decided until you confirm.");
        await Expect(LineOf(invoice, reference)).ToContainTextAsync("To decide");

        var reopened = await OpenLineAsync(invoiceNumber, reference);
        await Expect(LineOf(reopened, reference)).ToContainTextAsync("To decide");
        await Expect(Suggested(reopened)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Suggestion_ToReject_IsDecidedWhenFinanceWritesTheReasonAndConfirms()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var (invoiceNumber, earlier) = await ReceiveDuplicateAsync(reference);
        var invoice = await OpenLineAsync(invoiceNumber, reference);
        await ExplainButton(invoice, reference).ClickAsync();
        await Expect(Suggested(invoice)).ToBeVisibleAsync(new() { Timeout = AgentTimeout });

        await invoice.GetByRole(AriaRole.Textbox, new() { Name = $"Reason for rejecting {reference}" }).FillAsync($"Duplicates Carrier invoice {earlier}.");
        await RejectButton(invoice, reference).ClickAsync();

        var line = LineOf(invoice, reference);
        await Expect(line.GetByText("Rejected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(line).ToContainTextAsync($"Duplicates Carrier invoice {earlier}.");
    }

    // Wait is not a control on the form: a late Carrier cost matches the line by itself, so Finance does nothing and only Reject is offered.
    [Fact]
    public async Task Suggestion_OnAnUnknownBookingLine_IsToWait_PreSelectsNothing_AndNeverOffersAccept()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var invoiceNumber = await ReceiveWithSecondLineAsync(reference, 70m);
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(SuggestedKind(invoice, reference)).ToHaveTextAsync("Wait", new() { Timeout = AgentTimeout });
        await Expect(Suggested(invoice)).ToHaveCountAsync(0);
        await Expect(AcceptButton(invoice, reference)).ToHaveCountAsync(0);
        await Expect(RejectButton(invoice, reference)).ToBeVisibleAsync();
        await Expect(LineOf(invoice, reference)).ToContainTextAsync("To decide");
    }

    [Fact]
    public async Task Suggestion_OnADuplicateLine_NeverOffersAccept()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        var (invoiceNumber, _) = await ReceiveDuplicateAsync(reference);
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await ExplainButton(invoice, reference).ClickAsync();

        await Expect(SuggestedKind(invoice, reference)).ToHaveTextAsync("Reject", new() { Timeout = AgentTimeout });
        await Expect(AcceptButton(invoice, reference)).ToHaveCountAsync(0);
        await Expect(RejectButton(invoice, reference)).ToBeVisibleAsync();
    }
}
