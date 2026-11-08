using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Norvia.Erp.TestSupport;
using static Microsoft.Playwright.Assertions;

namespace Norvia.Erp.Web.UiTests.FinanceAgent;

/// <summary>
/// Finance drafts the rejection reason of a Carrier invoice line, against the scripted fake model (ADR 0041). Each journey records its own Carrier cost and Carrier invoice, opens a line that is
/// To decide in Finance's "Carrier invoices to decide" and chooses Draft reason in its Reject form: the Carrier-facing reason is prefilled and nothing is rejected, Finance edits it and rejects
/// with the button as ever, and only then is the line Rejected with the reason Finance confirmed. Every invoice has a second line that stays To decide, so its section stays on the page after
/// the first line is decided.
/// </summary>
public sealed class RejectionReasonJourneyTests(FinanceAgentUiFixture fixture) : ToDecidePage(fixture)
{
    private static ILocator ReasonBox(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Textbox, new() { Name = $"Reason for rejecting {reference}" });

    private static ILocator DraftButton(ILocator invoice, string reference) => invoice.GetByRole(AriaRole.Button, new() { Name = $"Draft reason for {reference}" });

    private static ILocator DraftNotice(ILocator invoice) => invoice.GetByTestId("reason-draft");

    /// <summary>Records the Carrier cost of <paramref name="reference"/>, receives an invoice whose first line claims it under a mangled reference (no dash) and a second line that stays To decide.</summary>
    private async Task<(string InvoiceNumber, string Mangled, string Reference)> ReceiveMangledAsync()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(reference, 100m);
        var mangled = reference.Replace("-", string.Empty, StringComparison.Ordinal);
        var second = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(second, SecondLineCost);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (mangled, 100m), (second, SecondLineClaim));
        return (invoiceNumber, mangled, reference);
    }

    [Fact]
    public async Task DraftReason_OnAMangledReference_PrefillsACarrierFacingReasonWithTheLikelyReference_AndRejectsNothing()
    {
        var (invoiceNumber, mangled, reference) = await ReceiveMangledAsync();
        var invoice = await OpenLineAsync(invoiceNumber, mangled);
        await Expect(ReasonBox(invoice, mangled)).ToHaveValueAsync("");
        await Expect(DraftNotice(invoice)).ToHaveCountAsync(0);

        await DraftButton(invoice, mangled).ClickAsync();

        var reason = ReasonBox(invoice, mangled);
        await Expect(reason).ToHaveValueAsync(new Regex(Regex.Escape($"The reference may be {reference}.")), new() { Timeout = AgentTimeout });
        await Expect(reason).ToHaveValueAsync(new Regex($"no booking under the reference {Regex.Escape(mangled)}"));
        await Expect(reason).ToHaveValueAsync(new Regex("Please send a new invoice, with a new invoice number"));
        await Expect(DraftNotice(invoice)).ToContainTextAsync("Nothing is sent to the Carrier until you do.");

        // The reason is Carrier-facing: no Rate Card, no price negotiation and no internal remark.
        await Expect(reason).Not.ToHaveValueAsync(new Regex("Rate Card|remark|negotiat", RegexOptions.IgnoreCase));

        // Nothing was rejected, not now and not after the ERP has been read again.
        await Expect(LineOf(invoice, mangled)).ToContainTextAsync("To decide");
        var reopened = await OpenLineAsync(invoiceNumber, mangled);
        await Expect(LineOf(reopened, mangled)).ToContainTextAsync("To decide");
    }

    [Fact]
    public async Task DraftReason_IsEditedByFinance_AndTheLineIsRejectedWithTheReasonFinanceConfirmed()
    {
        var (invoiceNumber, mangled, reference) = await ReceiveMangledAsync();
        var invoice = await OpenLineAsync(invoiceNumber, mangled);
        await DraftButton(invoice, mangled).ClickAsync();
        var reason = ReasonBox(invoice, mangled);
        await Expect(DraftNotice(invoice)).ToBeVisibleAsync(new() { Timeout = AgentTimeout });

        const string Edited = "Please resend this line in a new invoice with the right booking reference.";
        await reason.FillAsync($"{Edited} Booking {reference}.");
        await Expect(LineOf(invoice, mangled)).ToContainTextAsync("To decide");
        await RejectButton(invoice, mangled).ClickAsync();

        var line = LineOf(invoice, mangled);
        await Expect(line.GetByText("Rejected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(line).ToContainTextAsync($"{Edited} Booking {reference}.");
    }

    [Fact]
    public async Task DraftReason_ConfirmedAsDrafted_RejectsTheLineWithTheDraft()
    {
        var (invoiceNumber, mangled, reference) = await ReceiveMangledAsync();
        var invoice = await OpenLineAsync(invoiceNumber, mangled);
        await DraftButton(invoice, mangled).ClickAsync();
        await Expect(ReasonBox(invoice, mangled)).ToHaveValueAsync(new Regex(Regex.Escape(reference)), new() { Timeout = AgentTimeout });

        await RejectButton(invoice, mangled).ClickAsync();

        var line = LineOf(invoice, mangled);
        await Expect(line.GetByText("Rejected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = AgentTimeout });
        await Expect(line).ToContainTextAsync($"The reference may be {reference}.");
    }

    [Fact]
    public async Task DraftReason_OnADuplicate_NamesTheCarriersEarlierInvoice_AndAsksForNoNewInvoiceOfTheLine()
    {
        var reference = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(reference, 100m);
        var earlier = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(earlier, (reference, 100m));
        var second = CarrierInvoiceCalls.NewReference();
        await RecordCostAsync(second, SecondLineCost);
        var invoiceNumber = CarrierInvoiceCalls.NewInvoiceNumber();
        await ReceiveInvoiceAsync(invoiceNumber, (reference, 100m), (second, SecondLineClaim));
        var invoice = await OpenLineAsync(invoiceNumber, reference);

        await DraftButton(invoice, reference).ClickAsync();

        var reason = ReasonBox(invoice, reference);
        await Expect(reason).ToHaveValueAsync(new Regex($"already invoiced on your earlier invoice {Regex.Escape(earlier)}"), new() { Timeout = AgentTimeout });
        await Expect(reason).ToHaveValueAsync(new Regex("Please do not invoice it again"));
        await Expect(LineOf(invoice, reference)).ToContainTextAsync("To decide");
    }
}
