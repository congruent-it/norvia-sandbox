using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Runs;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// The Carriers after the Planner: the Planner releases the Shipments that went to a Carrier, and each Carrier answers, tracks and
/// delivers as its own Persona, through the Partner API only. It follows the Carrier funnel (<see cref="CarrierFunnel"/>): which
/// Shipments are released, confirmed, declined or left unanswered, tracked, delivered, proved, cancelled and which hit an
/// Exception, in exactly the configured shares. One Shipment at a time in plan order, so what happens depends on the seed and never on
/// which answer came first. What Norvia refuses is logged as an outcome and that Shipment's journey ends there; the run goes on.
/// </summary>
internal sealed class CarrierStage(IPartnerApi partner, ITmsApi tms, TimeProvider time, CarrierPacing pacing)
{
    private const string DeclineReason = "NO_CAPACITY";
    private const string CancellationReason = "The Carrier didn't make its collection";
    private const string ResolutionRemark = "Replanned: the Carrier booking was cancelled after the Carrier failed to collect";
    private const string ReservationsRemark = "Two pallets short";
    private const string Receiver = "Simulated consignee";

    // Events a minute apart, the last of them now: Norvia refuses a time in the future.
    private static readonly TimeSpan BetweenEvents = TimeSpan.FromMinutes(1);

    public async Task<CarrierResult> RunAsync(
        SimulationRun run, IReadOnlyList<PlannedShipment> carried, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        if (carried.Count == 0 || run.Settings.Carriers.ReleasePercent == 0)
        {
            return CarrierResult.None;
        }

        var shipments = await UsableAsync(carried, writer, cancellationToken);
        var journeys = CarrierFunnel.Plan(run.Settings.Carriers, run.Settings.Seed, shipments.Count);
        var tally = new Tally();
        var expected = new List<ExpectedException>();

        for (var index = 0; index < shipments.Count; index++)
        {
            if (stopSending.IsCancellationRequested)
            {
                return tally.Result(stopped: true);
            }

            if (await FollowAsync(run, shipments[index], journeys[index], tally, writer, cancellationToken) is { } expectedException)
            {
                expected.Add(expectedException);
            }
        }

        var stopped = await HandleExceptionsAsync(run, expected, writer, stopSending, cancellationToken);
        return tally.Result(stopped);
    }

    // A Carrier can only play its part as a Persona: its Shipments are left Planned, and the log says why, once for each Carrier.
    private async Task<List<PlannedShipment>> UsableAsync(IReadOnlyList<PlannedShipment> carried, RunLogWriter writer, CancellationToken cancellationToken)
    {
        foreach (var carrier in carried.Select(shipment => shipment.Carrier).Where(carrier => !carrier.HasPersona).DistinctBy(carrier => carrier.Id))
        {
            await writer.NoteAsync(time.GetUtcNow(), $"{carrier.Name} has no Persona to play it by: its Shipments are not released", cancellationToken);
        }

        return [.. carried.Where(shipment => shipment.Carrier.HasPersona)];
    }

    // The Exception this journey is expected to open, when its Carrier reported everything it should have.
    private async Task<ExpectedException?> FollowAsync(
        SimulationRun run, PlannedShipment shipment, CarrierJourney journey, Tally tally, RunLogWriter writer, CancellationToken cancellationToken)
    {
        if (!journey.Released)
        {
            return null;
        }

        var booking = shipment.Booking;
        var release = await tms.ReleaseAsync(shipment.ShipmentNumber, PlannerActions.ReleaseKey(run.Number, booking.Step), cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), PlannerActions.Release, booking, shipment.TransportOrderNumber, release, release.CarrierBookingNumber, cancellationToken);
        if (release is not { Outcome: LogOutcome.Released, CarrierBookingNumber: { } carrierBookingNumber })
        {
            return null;
        }

        tally.Released++;
        var reference = CarrierReferences.BookingReference(shipment.Carrier, run.Number, booking.Step);
        if (journey.Answer == CarrierAnswerKind.NoAnswer)
        {
            await writer.SilenceAsync(time.GetUtcNow(), shipment, CarrierActions.Answer, carrierBookingNumber, cancellationToken);
            return null;
        }

        var answer = await partner.AnswerBookingAsync(
            shipment.Carrier,
            journey.Answer == CarrierAnswerKind.Confirm
                ? new CarrierAnswerRequest(carrierBookingNumber, Confirm: true, reference, PlannedCollection: null, DeclineReason: null)
                : new CarrierAnswerRequest(carrierBookingNumber, Confirm: false, BookingReference: null, PlannedCollection: null, DeclineReason),
            cancellationToken);
        await writer.CarrierAsync(time.GetUtcNow(), shipment, CarrierActions.Answer, answer, carrierBookingNumber, cancellationToken);
        if (answer.Outcome != LogOutcome.Confirmed)
        {
            return null;
        }

        tally.Confirmed++;
        return await ReportAsync(run, shipment, journey, carrierBookingNumber, reference, tally, writer, cancellationToken);
    }

    private async Task<ExpectedException?> ReportAsync(
        SimulationRun run, PlannedShipment shipment, CarrierJourney journey, string carrierBookingNumber, string reference, Tally tally, RunLogWriter writer,
        CancellationToken cancellationToken)
    {
        var events = journey.Events;
        var now = time.GetUtcNow();
        for (var position = 0; position < events.Count; position++)
        {
            var carrierEvent = events[position];
            var tracking = new CarrierTrackingRequest(
                reference, carrierBookingNumber, CarrierReferences.EventId(shipment.Carrier, run.Number, shipment.Booking.Step, carrierEvent), carrierEvent,
                now - (BetweenEvents * (events.Count - 1 - position)), carrierEvent.HappensAtPickup() ? shipment.Booking.Pickup : shipment.Booking.Delivery, carrierEvent.Remark());
            var sent = await partner.SendTrackingAsync(shipment.Carrier, tracking, cancellationToken);
            await writer.CarrierAsync(time.GetUtcNow(), shipment, CarrierActions.Track, sent, $"{CarrierReferences.StatusCode(carrierEvent)} ({carrierEvent})", cancellationToken);
            if (sent.Outcome != LogOutcome.TrackingSent)
            {
                return null;
            }

            if (carrierEvent == CarrierEvent.Delivered)
            {
                tally.Delivered++;
            }
        }

        if (journey.ProofOfDelivery)
        {
            var reservations = journey.Exception == CarrierExceptionKind.Reservations;
            var proof = new CarrierProofRequest(
                reference, carrierBookingNumber, CarrierReferences.DocumentId(shipment.Carrier, run.Number, shipment.Booking.Step), Receiver, now,
                shipment.Booking.Delivery, reservations, reservations ? ReservationsRemark : null);
            var sent = await partner.SendProofOfDeliveryAsync(shipment.Carrier, proof, cancellationToken);
            await writer.CarrierAsync(time.GetUtcNow(), shipment, CarrierActions.ProveDelivery, sent, reservations ? "With reservations" : "Clean", cancellationToken);
            if (sent.Outcome != LogOutcome.ProofOfDeliverySent)
            {
                return null;
            }
        }

        return journey.Exception is { } kind ? new ExpectedException(shipment, kind, journey.Cancel) : null;
    }

    // Norvia processes tracking after it accepts it, so the Exceptions show up a little later: the Planner asks a few times.
    private async Task<bool> HandleExceptionsAsync(
        SimulationRun run, List<ExpectedException> expected, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var pending = expected;
        for (var attempt = 1; pending.Count > 0 && attempt <= pacing.ExceptionPollAttempts; attempt++)
        {
            if (attempt > 1 && pacing.ExceptionPollInterval > TimeSpan.Zero && await StoppedWhileWaitingAsync(stopSending))
            {
                return true;
            }

            var open = await tms.GetOpenExceptionsAsync(cancellationToken);
            if (open is null)
            {
                await writer.NoteAsync(time.GetUtcNow(), "The TMS couldn't tell the Open Exceptions: the Planner leaves the Exceptions as they are", cancellationToken);
                return false;
            }

            var next = new List<ExpectedException>();
            foreach (var wanted in pending)
            {
                if (open.FirstOrDefault(candidate => wanted.IsOpenedBy(candidate)) is not { } found)
                {
                    next.Add(wanted);
                    continue;
                }

                await FoundAsync(run, wanted, found, writer, cancellationToken);
            }

            pending = next;
        }

        foreach (var missing in pending)
        {
            await writer.NoteAsync(
                time.GetUtcNow(), $"The {missing.Kind} Exception of {missing.Shipment.ShipmentNumber} didn't open in time: the Planner leaves it", cancellationToken);
        }

        return false;
    }

    private async Task<bool> StoppedWhileWaitingAsync(CancellationToken stopSending)
    {
        try
        {
            await Task.Delay(pacing.ExceptionPollInterval, time, stopSending);
            return false;
        }
        catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
        {
            return true;
        }
    }

    private async Task FoundAsync(SimulationRun run, ExpectedException wanted, OpenExceptionSummary found, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var shipment = wanted.Shipment;
        var booking = shipment.Booking;
        await writer.PlanningAsync(
            time.GetUtcNow(), PlannerActions.FindException, booking, shipment.TransportOrderNumber,
            new PlannerAnswer(LogOutcome.ExceptionFound, null, shipment.ShipmentNumber, null), $"{found.ExceptionNumber}: {wanted.Kind}", cancellationToken);
        if (!wanted.Cancel)
        {
            return;
        }

        var cancelled = await tms.CancelCarrierBookingAsync(found.ExceptionNumber, CancellationReason, PlannerActions.CancellationKey(run.Number, booking.Step), cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), PlannerActions.CancelCarrierBooking, booking, shipment.TransportOrderNumber, cancelled, found.ExceptionNumber, cancellationToken);
        if (cancelled.Outcome != LogOutcome.BookingCancelled)
        {
            return;
        }

        var resolved = await tms.ResolveReplannedAsync(found.ExceptionNumber, ResolutionRemark, cancellationToken);
        await writer.PlanningAsync(time.GetUtcNow(), PlannerActions.ResolveException, booking, shipment.TransportOrderNumber, resolved, found.ExceptionNumber, cancellationToken);
    }

    private sealed record ExpectedException(PlannedShipment Shipment, CarrierExceptionKind Kind, bool Cancel)
    {
        public bool IsOpenedBy(OpenExceptionSummary open) =>
            open.ShipmentNumber == Shipment.ShipmentNumber && Normalised(open.Type).Equals(Kind.ToString(), StringComparison.OrdinalIgnoreCase);

        // The TMS's type may read "CollectionFailed" or "Collection failed".
        private static string Normalised(string type) => new([.. type.Where(char.IsLetter)]);
    }

    private sealed class Tally
    {
        public int Released { get; set; }

        public int Confirmed { get; set; }

        public int Delivered { get; set; }

        public CarrierResult Result(bool stopped) => new(stopped, Released, Confirmed, Delivered);
    }
}
