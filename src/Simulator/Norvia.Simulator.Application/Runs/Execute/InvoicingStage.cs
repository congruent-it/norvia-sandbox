using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// Finance after the deliveries, through the ERP API only: for the Shippers the funnel picks (<see cref="InvoicingFunnel"/>) it runs <b>Invoice now</b>, asking
/// again for a while because a delivered Transport Order becomes a Waiting line a little later, and for some of the invoices it issues a partial Credit note on
/// the first line, with a reason. Then it looks in each Shipper Persona's invoices inbox, and the log shows each document as it arrives there. One Shipper at a
/// time, in ID order, so what happens depends on the seed. A refusal is logged and Finance goes on.
/// </summary>
internal sealed class InvoicingStage(IErpApi erp, IShipperInbox inbox, TimeProvider time, DriverPacing pacing)
{
    /// <summary>The share of the line's total a Credit note credits: a partial one.</summary>
    private const decimal CreditShare = 0.25m;

    public async Task<InvoicingResult> RunAsync(
        SimulationRun run, IReadOnlyList<InvoicedShipper> shippers, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        if (shippers.Count == 0)
        {
            return InvoicingResult.None;
        }

        var journeys = InvoicingFunnel.Plan(run.Settings.Invoicing, run.Settings.Seed, shippers.Count);
        var expected = new List<Expected>();
        var invoiced = 0;
        var credited = 0;

        // Every Shipper picked is asked in each round, in ID order, so the wait for Transport Orders to become Waiting lines is the one wait, however many Shippers there are.
        var pending = journeys.Where(journey => journey.Invoice).ToList();
        InvoiceNowAnswer? last = null;
        for (var attempt = 1; pending.Count > 0 && attempt <= pacing.PollAttempts; attempt++)
        {
            if (attempt > 1 && !await PauseAsync(stopSending))
            {
                return new InvoicingResult(true, invoiced, credited, 0);
            }

            var next = new List<InvoicingJourney>();
            foreach (var journey in pending)
            {
                if (stopSending.IsCancellationRequested)
                {
                    return new InvoicingResult(true, invoiced, credited, 0);
                }

                var shipper = shippers[journey.Index];
                var answer = await erp.InvoiceNowAsync(shipper.ShipperId, cancellationToken);
                last = answer;
                if (answer.Outcome == LogOutcome.NothingToInvoice)
                {
                    next.Add(journey);
                    continue;
                }

                await LogInvoiceNowAsync(shipper, answer, writer, cancellationToken);
                if (answer is not { Outcome: LogOutcome.InvoiceIssued, InvoiceNumber: { } invoiceNumber })
                {
                    continue;
                }

                invoiced++;
                var documents = new List<string> { invoiceNumber };
                if (journey.CreditNote && await CreditAsync(run, shipper, answer, writer, cancellationToken) is { } creditNoteNumber)
                {
                    credited++;
                    documents.Add(creditNoteNumber);
                }

                expected.Add(new Expected(shipper, documents));
            }

            pending = next;
        }

        // Still nothing Waiting for these after the last round: one line each.
        foreach (var journey in pending)
        {
            await LogInvoiceNowAsync(shippers[journey.Index], last!, writer, cancellationToken);
        }

        var received = await WatchInboxesAsync(expected, writer, stopSending, cancellationToken);
        return new InvoicingResult(received.Stopped, invoiced, credited, received.Count);
    }

    private async Task LogInvoiceNowAsync(InvoicedShipper shipper, InvoiceNowAnswer answer, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var detail = answer.Outcome switch
        {
            LogOutcome.InvoiceIssued => $"{answer.InvoiceNumber}: {answer.Lines.Count} lines, {Euros(answer.TotalWithoutVat)} without VAT",
            LogOutcome.NothingToInvoice => $"No Waiting lines for {shipper.ShipperId} after {pacing.PollAttempts} tries",
            _ => answer.Detail,
        };
        await writer.InvoicingAsync(
            time.GetUtcNow(), InvoicingActions.FinanceActor, InvoicingActions.InvoiceNow, answer.Outcome, answer.CorrelationId, answer.Lines.Count > 0 ? answer.Lines[0].ShipperReference : null, null, Prefixed(shipper, detail),
            cancellationToken);
    }

    // A partial Credit note on the invoice's first line: a quarter of its total, in cents, with a reason. The Credit note's number, or null when the ERP refused.
    private async Task<string?> CreditAsync(SimulationRun run, InvoicedShipper shipper, InvoiceNowAnswer invoice, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var line = invoice.Lines[0];
        var amount = Math.Max(0.01m, Math.Round(line.Total * CreditShare, 2, MidpointRounding.AwayFromZero));
        var reason = $"Simulated Rate Card correction in run {run.Number}";
        var answer = await erp.IssueCreditNoteAsync(invoice.InvoiceNumber!, line.TransportOrderNumber, amount, reason, cancellationToken);
        var detail = answer.Outcome == LogOutcome.CreditNoteIssued
            ? $"{answer.CreditNoteNumber} on {invoice.InvoiceNumber}: {Euros(amount)} of {Euros(line.Total)} without VAT. {reason}"
            : $"{invoice.InvoiceNumber}: {answer.Detail}";
        await writer.InvoicingAsync(
            time.GetUtcNow(), InvoicingActions.FinanceActor, InvoicingActions.IssueCreditNote, answer.Outcome, answer.CorrelationId, line.ShipperReference, line.TransportOrderNumber, Prefixed(shipper, detail),
            cancellationToken);
        return answer.Outcome == LogOutcome.CreditNoteIssued ? answer.CreditNoteNumber : null;
    }

    // The documents travel through the Shipper invoice Logic App, so the inboxes are asked every interval, up to the attempts, and each document is logged once, as it arrives.
    private async Task<Watched> WatchInboxesAsync(List<Expected> expected, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var received = 0;
        var pending = expected.ToList();
        for (var attempt = 1; pending.Count > 0 && attempt <= pacing.PollAttempts; attempt++)
        {
            if (attempt > 1 && !await PauseAsync(stopSending))
            {
                return new Watched(true, received);
            }

            var next = new List<Expected>();
            foreach (var shipper in pending)
            {
                var documents = await inbox.ListAsync(shipper.Shipper.ShipperId, cancellationToken);
                if (documents is null)
                {
                    await writer.NoteAsync(time.GetUtcNow(), $"The invoices inbox of {shipper.Shipper.PersonaName} couldn't be read: its documents aren't shown arriving", cancellationToken);
                    continue;
                }

                var waiting = new List<string>();
                foreach (var number in shipper.DocumentNumbers)
                {
                    if (documents.FirstOrDefault(document => document.DocumentNumber == number) is not { } found)
                    {
                        waiting.Add(number);
                        continue;
                    }

                    received++;
                    var creditNote = found.DocumentType == "CREDIT_NOTE";
                    await writer.InvoicingAsync(
                        time.GetUtcNow(), shipper.Shipper.PersonaName, creditNote ? InvoicingActions.ReceiveCreditNote : InvoicingActions.ReceiveInvoice, LogOutcome.InboxReceived,
                        found.CorrelationId, null, null, $"{found.DocumentNumber} is in the invoices inbox", cancellationToken);
                }

                if (waiting.Count > 0)
                {
                    next.Add(shipper with { DocumentNumbers = waiting });
                }
            }

            pending = next;
        }

        foreach (var missing in pending)
        {
            await writer.NoteAsync(
                time.GetUtcNow(), $"{string.Join(", ", missing.DocumentNumbers)} didn't reach the invoices inbox of {missing.Shipper.PersonaName} in time", cancellationToken);
        }

        return new Watched(false, received);
    }

    // False when the run was cancelled while waiting.
    private async Task<bool> PauseAsync(CancellationToken stopSending)
    {
        if (stopSending.IsCancellationRequested)
        {
            return false;
        }

        if (pacing.PollInterval > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(pacing.PollInterval, time, stopSending);
            }
            catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
            {
                return false;
            }
        }

        return true;
    }

    private static string? Prefixed(InvoicedShipper shipper, string? detail) => detail is null ? $"For {shipper.PersonaName}" : $"For {shipper.PersonaName}. {detail}";

    private static string Euros(decimal amount) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"EUR {amount:0.00}");

    private sealed record Expected(InvoicedShipper Shipper, IReadOnlyList<string> DocumentNumbers);

    private sealed record Watched(bool Stopped, int Count);
}
