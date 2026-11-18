extern alias SimulatorWeb;

using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// The Simulator's Carrier invoicing at its boundary (#632, E2): after a Carrier run delivers, each delivering Carrier Persona sends an invoice through the
/// Partner API, Finance decides the lines left To decide through the ERP API, and the log shows each line's outcome as it arrives on the Carrier Persona's
/// invoices page, having gone through the Carrier invoice Logic App's stand-in, the ERP and the Carrier invoice outcome Logic App's stand-in.
/// </summary>
[Collection("Simulator runs")]
public sealed class CarrierInvoicingTests(SimulatorFixture fixture)
{
    // The Shipper whose every Lane the Carriers' Rate Cards cover, so the TMS can price each release and the Carriers deliver at a price Norvia offered.
    private const string PricedShipper = "SHP-0001";

    private static readonly TimeSpan InvoicingPatience = TimeSpan.FromMinutes(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The ERP knows a Carrier cost a little after the delivery (the timing note in docs/operations/simulator.md), so the last delivery's line can come back
    // UNKNOWN_BOOKING, which Finance rejects whatever the share. The tests therefore leave such lines out of what they count.
    private const string UnknownBooking = "UNKNOWN_BOOKING";

    private static bool IsUnknown(LogLine line) => line.Detail!.Contains(UnknownBooking, StringComparison.Ordinal);

    private static bool IsCarrierInvoicing(LogLine line) =>
        line.Action is InvoicingActions.SendCarrierInvoice or InvoicingActions.AcceptCarrierInvoiceLine
            or InvoicingActions.RejectCarrierInvoiceLine or InvoicingActions.ReceiveInvoiceOutcome;

    [Fact]
    public async Task InvoicesAsAgreed_AreMatched_AndTheirOutcomesArriveOnTheCarriersInvoicesPage_WithoutFinanceDecidingAnything()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 6, Runs.NextSeed(), Shares(edited: 0, rejected: 0));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var lines = log.Where(IsCarrierInvoicing).OrderBy(line => line.Sequence).ToList();

        var sent = lines.Where(line => line.Action == InvoicingActions.SendCarrierInvoice).ToList();
        sent.Should().NotBeEmpty("a Carrier Persona delivered");
        sent.Should().OnlyContain(line => line.Outcome == LogOutcome.CarrierInvoiced);
        sent.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId), "the Partner API's answer opens the invoice's trail");
        sent.Select(line => line.Actor).Should().OnlyHaveUniqueItems("each delivering Carrier Persona sends one invoice");
        sent.Should().OnlyContain(line => line.Detail!.Contains("-SIM", StringComparison.Ordinal));
        sent.Should().Contain(line => line.Detail!.Contains("MATCHED", StringComparison.Ordinal));
        sent.Should().NotContain(line => line.Detail!.Contains("PRICE_MISMATCH", StringComparison.Ordinal), "nothing was edited");

        lines.Should().NotContain(line => line.Action == InvoicingActions.AcceptCarrierInvoiceLine, "no line has a price to accept");
        lines.Where(line => line.Outcome == LogOutcome.LineRejected).Should().OnlyContain(line => IsUnknown(line), "only a line the ERP doesn't know yet is left To decide");

        var outcomes = lines.Where(line => line.Action == InvoicingActions.ReceiveInvoiceOutcome).ToList();
        outcomes.Should().NotBeEmpty();
        outcomes.Should().OnlyContain(line => line.Outcome == LogOutcome.OutcomeReceived);
        outcomes.Should().Contain(line => line.Detail!.StartsWith("MATCHED:", StringComparison.Ordinal));
        outcomes.Where(line => !line.Detail!.StartsWith("MATCHED:", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.Detail!.StartsWith("REJECTED:", StringComparison.Ordinal) && IsUnknown(line));
        outcomes.Select(line => line.Actor).Should().BeSubsetOf(sent.Select(line => line.Actor), "the outcomes arrive on the page of the Carrier that sent the invoice");
        outcomes.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId));
        outcomes.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.ShipmentNumber == null ? line.TransportOrderNumber : line.ShipmentNumber));

        AssertTelemetry(simulator);
    }

    [Fact]
    public async Task EditedInvoices_HaveTheirPriceMismatchesAccepted_AtTheAgreedAmount_WithARemark_AndTheOutcomeArrivesAsAcceptedAdjusted()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 6, Runs.NextSeed(), Shares(edited: 100, rejected: 0));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var lines = log.Where(IsCarrierInvoicing).OrderBy(line => line.Sequence).ToList();

        var sent = lines.Where(line => line.Action == InvoicingActions.SendCarrierInvoice).ToList();
        sent.Should().NotBeEmpty();
        sent.Should().OnlyContain(line => line.Detail!.Contains("PRICE_MISMATCH", StringComparison.Ordinal) || IsUnknown(line), "every invoice carries one edited amount, unless the ERP doesn't know its booking yet");
        sent.Should().Contain(line => line.Detail!.Contains("PRICE_MISMATCH", StringComparison.Ordinal));

        var accepted = lines.Where(line => line.Action == InvoicingActions.AcceptCarrierInvoiceLine).ToList();
        accepted.Should().NotBeEmpty("every invoice carries a Price mismatch for Finance to accept");
        accepted.Count.Should().BeLessThanOrEqualTo(sent.Count, "one line of each invoice is edited");
        accepted.Should().OnlyContain(line => line.Outcome == LogOutcome.LineAccepted);
        accepted.Should().OnlyContain(line => line.Actor == InvoicingActions.FinanceActor);
        accepted.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.CorrelationId) && !string.IsNullOrWhiteSpace(line.TransportOrderNumber));
        accepted.Should().OnlyContain(line => line.Detail!.Contains("accepted at the agreed", StringComparison.Ordinal) && line.Detail.Contains("Agreed price applies", StringComparison.Ordinal),
            "Finance accepts at the agreed amount, with a remark");
        lines.Where(line => line.Outcome == LogOutcome.LineRejected).Should().OnlyContain(line => IsUnknown(line), "Finance only rejects what it can't accept");

        var outcomes = lines.Where(line => line.Action == InvoicingActions.ReceiveInvoiceOutcome).ToList();
        outcomes.Count(line => line.Detail!.StartsWith("ACCEPTED_ADJUSTED:", StringComparison.Ordinal)).Should().Be(accepted.Count, "each accepted line reaches the page adjusted");
        outcomes.Where(line => !line.Detail!.StartsWith("ACCEPTED_ADJUSTED:", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.Detail!.StartsWith("MATCHED:", StringComparison.Ordinal) || (line.Detail.StartsWith("REJECTED:", StringComparison.Ordinal) && IsUnknown(line)));
        outcomes.Where(line => line.Detail!.StartsWith("ACCEPTED_ADJUSTED:", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.Detail!.Contains("Agreed price applies", StringComparison.Ordinal), "Finance's remark travels to the Carrier's page");

        // Each decision comes before the outcome that tells the Carrier of it.
        lines.FindLastIndex(line => line.Action == InvoicingActions.AcceptCarrierInvoiceLine)
            .Should().BeLessThan(lines.FindIndex(line => line.Action == InvoicingActions.ReceiveInvoiceOutcome));

        AssertTelemetry(simulator);
    }

    [Fact]
    public async Task WhenFinanceRejectsEveryLineLeft_TheOutcomesArriveAsRejected_WithTheReason()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 6, Runs.NextSeed(), Shares(edited: 100, rejected: 100));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var lines = log.Where(IsCarrierInvoicing).OrderBy(line => line.Sequence).ToList();

        var rejected = lines.Where(line => line.Action == InvoicingActions.RejectCarrierInvoiceLine).ToList();
        rejected.Should().NotBeEmpty();
        rejected.Should().OnlyContain(line => line.Outcome == LogOutcome.LineRejected && line.Actor == InvoicingActions.FinanceActor);
        rejected.Should().OnlyContain(line => line.Detail!.Contains("Simulated rejection in run", StringComparison.Ordinal), "a rejection has a reason");
        lines.Should().NotContain(line => line.Action == InvoicingActions.AcceptCarrierInvoiceLine);

        var outcomes = lines.Where(line => line.Action == InvoicingActions.ReceiveInvoiceOutcome).ToList();
        outcomes.Count(line => line.Detail!.StartsWith("REJECTED:", StringComparison.Ordinal)).Should().Be(rejected.Count);
        outcomes.Where(line => line.Detail!.StartsWith("REJECTED:", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.Detail!.Contains("Simulated rejection in run", StringComparison.Ordinal), "Finance's reason travels to the Carrier's page");
    }

    [Fact]
    public async Task WithoutDeliveries_NoCarrierInvoicingStageHappens()
    {
        await using var simulator = await fixture.StartSimulatorWithSeedAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var carriers = new Norvia.Simulator.Domain.Carriers.CarrierShares(
            ReleasePercent: 100, ConfirmPercent: 100, DeclinePercent: 0, TrackedPercent: 100, ExceptionPercent: 0,
            DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);
        var settings = Runs.Carried(orders: 3, Runs.NextSeed(), carriers) with { Invoicing = Shares(edited: 100, rejected: 100) };

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        log.Should().NotContain(line => IsCarrierInvoicing(line), "the run delivered nothing through a Carrier");
    }

    [Fact]
    public async Task WhenTheErpCannotBeReached_FinanceLogsEachDecisionAsNotReceived_AndTheRunStillCompletes()
    {
        await using var simulator = await fixture.StartSimulatorWithoutErpAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 4, Runs.NextSeed(), Shares(edited: 100, rejected: 0));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        var lines = log.Where(IsCarrierInvoicing).ToList();
        lines.Should().Contain(line => line.Action == InvoicingActions.SendCarrierInvoice && line.Outcome == LogOutcome.CarrierInvoiced, "the Partner API doesn't need the Simulator's ERP");
        lines.Should().Contain(
            line => (line.Action == InvoicingActions.AcceptCarrierInvoiceLine || line.Action == InvoicingActions.RejectCarrierInvoiceLine)
                && line.Outcome == LogOutcome.InvoicingNotReceived);
        lines.Should().NotContain(line => line.Outcome == LogOutcome.LineAccepted || line.Outcome == LogOutcome.LineRejected);
    }

    [Fact]
    public async Task WhenTheCarriersInboxCannotBeRead_NoInvoiceIsSent_AndTheLogSaysSo()
    {
        await using var simulator = await fixture.StartSimulatorWithoutPartnerPortalAsync(Ct, SeedEdits.OnlyShippers(PricedShipper));
        var settings = Runs.Invoiced(orders: 3, Runs.NextSeed(), Shares(edited: 100, rejected: 0));

        var (_, run, log) = await simulator.RunToEndAsync(settings, Ct, InvoicingPatience);

        run.Status.Should().Be(SimulationRunStatus.Completed);
        log.Should().NotContain(line => IsCarrierInvoicing(line), "a Carrier can't price its invoice without its booking inbox");
        log.Should().Contain(line => line.Outcome == LogOutcome.Info && line.Action.Contains("booking inbox", StringComparison.Ordinal) && line.Action.Contains("no Carrier invoice", StringComparison.Ordinal));
    }

    private static InvoicingShares Shares(int edited, int rejected) =>
        new(InvoicePercent: 0, CreditNotePercent: 0, EditedCarrierInvoicePercent: edited, RejectedCarrierLinePercent: rejected);

    // Logs and traces carry the tracking keys of §7 for the Carrier invoice and the decisions on its lines.
    private static void AssertTelemetry(SimulatorHost simulator)
    {
        simulator.Telemetry.Entries.Should().Contain(
            entry => entry.Contains("flow=carrier-invoice", StringComparison.Ordinal) && entry.Contains("documentNumber=", StringComparison.Ordinal)
                && entry.Contains("-SIM", StringComparison.Ordinal),
            "the Simulator's trace of a Carrier invoice names the invoice number");
    }
}
