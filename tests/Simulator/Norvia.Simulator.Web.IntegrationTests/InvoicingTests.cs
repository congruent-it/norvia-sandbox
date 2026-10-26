extern alias SimulatorWeb;

using System.Globalization;
using System.Text.RegularExpressions;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The Simulator's Finance at its boundary (#601): after a Carrier run delivers, Finance runs Invoice now through the ERP API, issues a partial Credit note on
/// the first line of some invoices, and the log shows each document as it arrives in the Shipper Persona's invoices inbox in the Partner portal, having gone
/// through the TMS, the Billing Logic App's stand-in, the ERP and the Shipper invoice Logic App's stand-in.
/// </summary>
[Collection("Simulator runs")]
public sealed partial class InvoicingTests(SimulatorFixture fixture)
{
    // The Shipper whose every Lane the Carriers' Rate Cards cover, so the TMS can price each release and the Transport Orders become Billable.
    private const string PricedShipper = "SHP-0001";

    private static readonly TimeSpan InvoicingPatience = TimeSpan.FromMinutes(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InvoiceNowAndACreditNote_AreIssuedByTheErp_AndArriveInTheShippersInbox_InTheRunLog()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 6, Runs.NextSeed(), new InvoicingShares(InvoicePercent: 100, CreditNotePercent: 100));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var invoicing = log.Where(IsShipperInvoicing).OrderBy(line => line.Sequence).ToList();

        var invoice = invoicing.Should().ContainSingle(line => line.Outcome == LogOutcome.InvoiceIssued, "one Shipper booked, so one invoice").Subject;
        invoice.Actor.Should().Be(InvoicingActions.FinanceActor);
        invoice.Action.Should().Be(InvoicingActions.InvoiceNow);
        invoice.CorrelationId.Should().NotBeNullOrWhiteSpace("the ERP's answer opens the invoice's trail");
        var invoiceNumber = InvoiceNumber().Match(invoice.Detail!).Value;
        invoiceNumber.Should().NotBeEmpty("Invoice now reports the invoice number");
        invoice.Detail.Should().Contain("For ").And.Contain("without VAT");

        var creditNote = invoicing.Should().ContainSingle(line => line.Outcome == LogOutcome.CreditNoteIssued, "the share asked for a Credit note on the invoice").Subject;
        creditNote.Action.Should().Be(InvoicingActions.IssueCreditNote);
        creditNote.TransportOrderNumber.Should().NotBeNullOrWhiteSpace("a Credit note is on one line, which is one Transport Order");
        creditNote.CorrelationId.Should().NotBeNullOrWhiteSpace();
        var creditNoteNumber = CreditNoteNumber().Match(creditNote.Detail!).Value;
        creditNoteNumber.Should().NotBeEmpty();
        creditNote.Detail.Should().Contain($"on {invoiceNumber}").And.Contain("Simulated Rate Card correction", "a Credit note has a reason");
        invoicing.IndexOf(creditNote).Should().BeGreaterThan(invoicing.IndexOf(invoice), "the Credit note is on an invoice that exists");

        var amounts = CreditedAmounts().Match(creditNote.Detail!);
        amounts.Success.Should().BeTrue(creditNote.Detail);
        var credited = decimal.Parse(amounts.Groups["credited"].Value, CultureInfo.InvariantCulture);
        var lineTotal = decimal.Parse(amounts.Groups["total"].Value, CultureInfo.InvariantCulture);
        credited.Should().BeGreaterThan(0).And.BeLessThan(lineTotal, "a partial Credit note credits part of the line");

        var received = invoicing.Where(line => line.Outcome == LogOutcome.InboxReceived).ToList();
        received.Should().HaveCount(2, "the invoice and the Credit note both reach the inbox");
        received.Should().OnlyContain(line => line.Actor != InvoicingActions.FinanceActor, "the inbox belongs to the Shipper's Persona");
        received.Select(line => line.Actor).Distinct().Should().ContainSingle();
        received.Single(line => line.Action == InvoicingActions.ReceiveInvoice).Detail.Should().StartWith(invoiceNumber);
        received.Single(line => line.Action == InvoicingActions.ReceiveCreditNote).Detail.Should().StartWith(creditNoteNumber);
        received.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId), "the Shipper invoice Logic App carries the correlation ID on");

        log.OrderBy(line => line.Sequence).Last().Action.Should().Contain("1 invoices and 1 Credit notes issued, 2 in the Shippers' inboxes");
        AssertTelemetry(simulator);
    }

    [Fact]
    public async Task WithoutACreditNoteShare_TheInvoiceIsIssued_AndOnlyItArrivesInTheInbox()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 4, Runs.NextSeed(), new InvoicingShares(InvoicePercent: 100, CreditNotePercent: 0));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        log.Count(line => line.Outcome == LogOutcome.InvoiceIssued).Should().Be(1);
        log.Should().NotContain(line => line.Outcome == LogOutcome.CreditNoteIssued || line.Action == InvoicingActions.ReceiveCreditNote);
        log.Where(line => line.Outcome == LogOutcome.InboxReceived).Should().ContainSingle().Which.Action.Should().Be(InvoicingActions.ReceiveInvoice);
    }

    [Fact]
    public async Task WithoutAnInvoiceShare_FinanceDoesNothing_AndTheRunStillCompletes()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 4, Runs.NextSeed(), new InvoicingShares(InvoicePercent: 0, CreditNotePercent: 100));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        log.Should().NotContain(line => IsShipperInvoicing(line), "a Credit note is only ever on an invoice");
    }

    [Fact]
    public async Task TwoRunsWithTheSameSeed_InvoiceAndCreditTheSameWay()
    {
        var seed = Runs.NextSeed();
        var first = await InvoicingOfARunAsync(seed);
        var second = await InvoicingOfARunAsync(seed);

        first.Should().NotBeEmpty();
        second.Should().Equal(first, "the funnel follows the seed; only the documents' numbers differ");
    }

    // The Carriers' invoices ride on the same run and log their own lines (CarrierInvoicingTests); this class is about the Shippers' invoices.
    private static bool IsCarrierInvoicing(LogLine line) =>
        line.Action is InvoicingActions.SendCarrierInvoice or InvoicingActions.AcceptCarrierInvoiceLine or InvoicingActions.RejectCarrierInvoiceLine or InvoicingActions.ReceiveInvoiceOutcome;

    private static bool IsShipperInvoicing(LogLine line) => Runs.IsInvoicing(line) && !IsCarrierInvoicing(line);

    [Fact]
    public async Task WhenTheErpCannotBeReached_FinanceLogsIt_AndTheRunStillCompletes()
    {
        await using var simulator = await fixture.StartSimulatorWithoutErpAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 3, Runs.NextSeed(), new InvoicingShares(InvoicePercent: 100, CreditNotePercent: 100));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var lines = log.Where(IsShipperInvoicing).ToList();
        lines.Should().ContainSingle().Which.Outcome.Should().Be(LogOutcome.InvoicingNotReceived);
        lines[0].Actor.Should().Be(InvoicingActions.FinanceActor);
    }

    [Fact]
    public async Task WhenTheInvoicesInboxCannotBeRead_TheInvoiceIsStillIssued_AndTheLogSaysTheInboxIsUnreadable()
    {
        await using var simulator = await fixture.StartSimulatorWithoutPartnerPortalAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 3, Runs.NextSeed(), new InvoicingShares(InvoicePercent: 100, CreditNotePercent: 100));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        log.Any(line => line.Outcome == LogOutcome.InvoiceIssued).Should().BeTrue();
        log.Should().NotContain(line => line.Outcome == LogOutcome.InboxReceived);
        log.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("invoices inbox", StringComparison.Ordinal));
    }

    // What Finance did, without the numbers the ERP gives: who, which action, with what outcome, in order.
    private async Task<List<string>> InvoicingOfARunAsync(int seed)
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 4, seed, new InvoicingShares(InvoicePercent: 100, CreditNotePercent: 100));
        var (_, _, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);
        return [.. log.Where(IsShipperInvoicing).OrderBy(line => line.Sequence).Select(line => $"{line.Actor}|{line.Action}|{line.Outcome}")];
    }

    // Logs and traces carry the tracking keys of §7 for the ERP calls.
    private static void AssertTelemetry(SimulatorHost simulator)
    {
        var entries = simulator.Telemetry.Entries;
        entries.Should().Contain(
            entry => entry.Contains("flow=invoicing", StringComparison.Ordinal) && entry.Contains("documentNumber=INV-", StringComparison.Ordinal),
            "the Simulator's trace of Invoice now names the invoice number");
        entries.Should().Contain(
            entry => entry.Contains("flow=invoicing", StringComparison.Ordinal) && entry.Contains("documentNumber=CN-", StringComparison.Ordinal),
            "and its trace of the Credit note names the Credit note number");
    }

    [GeneratedRegex(@"INV-\d{4}-\d{6}")]
    private static partial Regex InvoiceNumber();

    [GeneratedRegex(@"CN-\d{4}-\d{6}")]
    private static partial Regex CreditNoteNumber();

    [GeneratedRegex(@"EUR (?<credited>\d+\.\d{2}) of EUR (?<total>\d+\.\d{2}) without VAT")]
    private static partial Regex CreditedAmounts();
}
