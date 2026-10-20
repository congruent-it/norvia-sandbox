using System.Globalization;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// The Carriers and Finance after the deliveries (E2). Each Carrier Persona that delivered sends one invoice through the Partner API for its delivered bookings, at the
/// price Norvia offered for each, and for some of the invoices (<see cref="CarrierInvoicingFunnel"/>) it edits the amount of one line, a Price mismatch on purpose. Finance,
/// through the ERP API only, then decides the lines left To decide: it accepts a Price mismatch at the agreed amount with a remark, or rejects the line with a reason.
/// Last, the Carrier Persona's invoices page is read, and each line's outcome is logged as it arrives there. One Carrier at a time, in ID order, so what happens depends
/// on the seed. A refusal is logged and the stage goes on.
/// </summary>
internal sealed class CarrierInvoicingStage(IPartnerApi partner, IErpApi erp, ICarrierInbox inbox, TimeProvider time, DriverPacing pacing)
{
    private const string EditedDescription = "Waiting time included";
    private const string PriceMismatch = "PRICE_MISMATCH";

    public async Task<CarrierInvoicingResult> RunAsync(
        SimulationRun run, IReadOnlyList<CarrierDelivery> deliveries, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var invoices = new List<Invoice>();
        foreach (var group in deliveries.Where(delivery => delivery.Carrier.HasPersona).GroupBy(delivery => delivery.Carrier.Id).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (stopSending.IsCancellationRequested)
            {
                return new CarrierInvoicingResult(true, 0, 0, 0);
            }

            if (await PricedAsync(group.First().Carrier, [.. group], writer, cancellationToken) is { } priced)
            {
                invoices.Add(priced);
            }
        }

        if (invoices.Count == 0)
        {
            return CarrierInvoicingResult.None;
        }

        var edited = CarrierInvoicingFunnel.EditedLines(run.Settings.Invoicing, run.Settings.Seed, [.. invoices.Select(invoice => invoice.Lines.Count)]);
        var sent = new List<Sent>();
        for (var index = 0; index < invoices.Count; index++)
        {
            if (stopSending.IsCancellationRequested)
            {
                return new CarrierInvoicingResult(true, sent.Count, 0, 0);
            }

            if (await SendAsync(run, invoices[index], edited[index], writer, cancellationToken) is { } answered)
            {
                sent.Add(answered);
            }
        }

        var decided = await DecideAsync(run, sent, writer, stopSending, cancellationToken);
        if (decided.Stopped)
        {
            return new CarrierInvoicingResult(true, sent.Count, decided.Count, 0);
        }

        var received = await WatchPagesAsync([.. sent.Where(invoice => invoice.Approved)], writer, stopSending, cancellationToken);
        return new CarrierInvoicingResult(received.Stopped, sent.Count, decided.Count, received.Count);
    }

    // What the Carrier invoices for its deliveries: the price Norvia offered in each booking request, read from its inbox.
    private async Task<Invoice?> PricedAsync(SimulatedCarrier carrier, List<CarrierDelivery> delivered, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var prices = await inbox.AgreedPricesAsync(carrier.Id, cancellationToken);
        if (prices is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), $"The booking inbox of {NameOf(carrier)} couldn't be read: it sends no Carrier invoice", cancellationToken);
            return null;
        }

        var lines = new List<PricedLine>();
        foreach (var delivery in delivered)
        {
            if (prices.TryGetValue(delivery.CarrierBookingNumber, out var price))
            {
                lines.Add(new PricedLine(delivery, price));
                continue;
            }

            await writer.NoteAsync(
                time.GetUtcNow(), $"{NameOf(carrier)} has no price for Carrier booking {delivery.CarrierBookingNumber} in its inbox: the booking isn't invoiced", cancellationToken);
        }

        return lines.Count == 0 ? null : new Invoice(carrier, lines);
    }

    private async Task<Sent?> SendAsync(SimulationRun run, Invoice invoice, int? editedLine, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var carrier = invoice.Carrier;
        var number = CarrierReferences.InvoiceNumber(carrier, run.Number);
        var lines = invoice.Lines
            .Select((line, position) => position == editedLine
                ? new CarrierInvoiceLineRequest(line.Delivery.BookingReference, Edited(line.Price), EditedDescription)
                : new CarrierInvoiceLineRequest(line.Delivery.BookingReference, line.Price, null))
            .ToList();
        var answer = await partner.SendCarrierInvoiceAsync(
            carrier, new CarrierInvoiceRequest(number, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), lines), cancellationToken);

        var detail = answer.Outcome == LogOutcome.CarrierInvoiced
            ? $"{number} is {answer.Status}: {string.Join(", ", answer.Lines.Select(line => $"{line.BookingReference} {line.Result} {Euros(line.Amount)}"))}"
            : $"{number}: {answer.Detail}";
        await writer.InvoicingAsync(
            time.GetUtcNow(), NameOf(carrier), InvoicingActions.SendCarrierInvoice, answer.Outcome, answer.CorrelationId, null, null, detail, cancellationToken);
        return answer.Outcome == LogOutcome.CarrierInvoiced ? new Sent(invoice, number, answer) : null;
    }

    // Finance decides the lines left To decide, all invoices together, so the share it rejects is exactly the share asked for.
    private async Task<Watched> DecideAsync(SimulationRun run, List<Sent> sent, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var waiting = sent.SelectMany(invoice => invoice.Answer.Lines.Where(line => !line.Matched).Select(line => (Invoice: invoice, Line: line))).ToList();
        var rejections = CarrierInvoicingFunnel.RejectedLines(run.Settings.Invoicing, run.Settings.Seed, waiting.Count);
        var decided = 0;
        for (var index = 0; index < waiting.Count; index++)
        {
            if (stopSending.IsCancellationRequested)
            {
                return new Watched(true, decided);
            }

            var (invoice, line) = waiting[index];
            var priced = invoice.Source.Lines.First(candidate => candidate.Delivery.BookingReference == line.BookingReference);
            // Only a Price mismatch can be accepted: the other results have no Carrier cost left to accept against.
            var reject = rejections.Contains(index) || line.Result != PriceMismatch;
            var answer = reject
                ? await RejectAsync(run, invoice, line, writer, cancellationToken)
                : await AcceptAsync(run, invoice, line, priced, writer, cancellationToken);
            if (answer.Outcome is LogOutcome.LineAccepted or LogOutcome.LineRejected)
            {
                decided++;
            }
            else
            {
                invoice.Undecided++;
            }
        }

        return new Watched(false, decided);
    }

    private async Task<LineDecisionAnswer> AcceptAsync(SimulationRun run, Sent invoice, CarrierInvoiceLineAnswer line, PricedLine priced, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var remark = $"Agreed price applies (simulated, run {run.Number})";
        var answer = await erp.AcceptCarrierInvoiceLineAsync(
            invoice.Source.Carrier.Id, invoice.Number, line.BookingReference, priced.Price, remark, cancellationToken);
        var detail = answer.Outcome == LogOutcome.LineAccepted
            ? $"{invoice.Number} {line.BookingReference}: claimed {Euros(line.Amount)}, accepted at the agreed {Euros(priced.Price)}. {remark}"
            : $"{invoice.Number} {line.BookingReference}: {answer.Detail}";
        await LogDecisionAsync(InvoicingActions.AcceptCarrierInvoiceLine, priced.Delivery, answer, detail, writer, cancellationToken);
        return answer;
    }

    private async Task<LineDecisionAnswer> RejectAsync(SimulationRun run, Sent invoice, CarrierInvoiceLineAnswer line, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var reason = $"Simulated rejection in run {run.Number}: {line.Result}";
        var answer = await erp.RejectCarrierInvoiceLineAsync(invoice.Source.Carrier.Id, invoice.Number, line.BookingReference, reason, cancellationToken);
        var detail = answer.Outcome == LogOutcome.LineRejected
            ? $"{invoice.Number} {line.BookingReference}: claimed {Euros(line.Amount)}. {reason}"
            : $"{invoice.Number} {line.BookingReference}: {answer.Detail}";
        var delivery = invoice.Source.Lines.First(candidate => candidate.Delivery.BookingReference == line.BookingReference).Delivery;
        await LogDecisionAsync(InvoicingActions.RejectCarrierInvoiceLine, delivery, answer, detail, writer, cancellationToken);
        return answer;
    }

    private Task LogDecisionAsync(string action, CarrierDelivery delivery, LineDecisionAnswer answer, string detail, RunLogWriter writer, CancellationToken cancellationToken) =>
        writer.InvoicingAsync(
            time.GetUtcNow(), InvoicingActions.FinanceActor, action, answer.Outcome, answer.CorrelationId, delivery.Shipment.Booking.Reference, delivery.Shipment.TransportOrderNumber,
            $"For {NameOf(delivery.Carrier)}. {detail}", cancellationToken);

    // The outcome of an invoice reaches the Carrier through the Carrier invoice outcome Logic App, so the pages are asked every interval, up to the attempts, and each line is logged once, as it arrives.
    private async Task<Watched> WatchPagesAsync(List<Sent> approved, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var received = 0;
        var pending = approved;
        for (var attempt = 1; pending.Count > 0 && attempt <= pacing.PollAttempts; attempt++)
        {
            if (attempt > 1 && !await PauseAsync(stopSending))
            {
                return new Watched(true, received);
            }

            var next = new List<Sent>();
            foreach (var group in pending.GroupBy(invoice => invoice.Source.Carrier.Id))
            {
                var outcomes = await inbox.OutcomesAsync(group.Key, cancellationToken);
                if (outcomes is null)
                {
                    await writer.NoteAsync(
                        time.GetUtcNow(), $"The invoices page of {NameOf(group.First().Source.Carrier)} couldn't be read: its outcomes aren't shown arriving", cancellationToken);
                    continue;
                }

                foreach (var invoice in group)
                {
                    if (outcomes.FirstOrDefault(outcome => outcome.InvoiceNumber == invoice.Number) is not { } outcome)
                    {
                        next.Add(invoice);
                        continue;
                    }

                    received += await LogOutcomeAsync(invoice, outcome, writer, cancellationToken);
                }
            }

            pending = next;
        }

        foreach (var missing in pending)
        {
            await writer.NoteAsync(
                time.GetUtcNow(), $"The outcome of {missing.Number} didn't reach the invoices page of {NameOf(missing.Source.Carrier)} in time", cancellationToken);
        }

        return new Watched(false, received);
    }

    private async Task<int> LogOutcomeAsync(Sent invoice, CarrierInvoiceOutcome outcome, RunLogWriter writer, CancellationToken cancellationToken)
    {
        foreach (var line in outcome.Lines)
        {
            var delivery = invoice.Source.Lines.FirstOrDefault(candidate => candidate.Delivery.BookingReference == line.BookingReference)?.Delivery;
            var detail = $"{line.Result}: {outcome.InvoiceNumber} {line.BookingReference}, accepted {Euros(line.AcceptedAmount)}" + (line.Remark is null ? string.Empty : $". {line.Remark}");
            await writer.InvoicingAsync(
                time.GetUtcNow(), NameOf(invoice.Source.Carrier), InvoicingActions.ReceiveInvoiceOutcome, LogOutcome.OutcomeReceived, outcome.CorrelationId,
                delivery?.Shipment.Booking.Reference, delivery?.Shipment.TransportOrderNumber, detail, cancellationToken);
        }

        return outcome.Lines.Count;
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

    // A tenth more, at least a cent: a Price mismatch for sure.
    private static decimal Edited(decimal price) => price + Math.Max(0.01m, Math.Round(price * 0.1m, 2, MidpointRounding.AwayFromZero));

    private static string NameOf(SimulatedCarrier carrier) => carrier.PersonaName ?? carrier.Name;

    private static string Euros(decimal amount) => string.Create(CultureInfo.InvariantCulture, $"EUR {amount:0.00}");

    private sealed record PricedLine(CarrierDelivery Delivery, decimal Price);

    private sealed record Invoice(SimulatedCarrier Carrier, IReadOnlyList<PricedLine> Lines);

    private sealed class Sent(Invoice source, string number, CarrierInvoiceAnswer answer)
    {
        public Invoice Source { get; } = source;

        public string Number { get; } = number;

        public CarrierInvoiceAnswer Answer { get; } = answer;

        /// <summary>The lines Finance couldn't decide, because the ERP refused or couldn't be reached: the invoice then isn't Approved.</summary>
        public int Undecided { get; set; }

        /// <summary>Approved on receipt, when every line matched, or once Finance decided the last line left.</summary>
        public bool Approved => Undecided == 0;
    }

    private sealed record Watched(bool Stopped, int Count);
}
