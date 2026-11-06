using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Drivers;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Runs;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>
/// The Drivers of the Trips for today, through the Driver app API only, each as themselves: they wait for the Trip to reach the Driver app, start it, collect
/// and deliver every Shipment with a Proof of delivery or fail it, report damage and end the Trip. It follows the Driver funnel (<see cref="DriverFunnel"/>) in
/// exactly the configured shares. One Trip at a time in dispatch order, and one Shipment at a time, so what happens depends on the seed and never on which answer
/// came first. A report the Driver app refuses is logged and the Driver goes on; the Trip then can't end, which the log shows.
/// </summary>
internal sealed class DriverStage(IDriverApp app, TimeProvider time, DriverPacing pacing)
{
    // The fixed words a Driver sends when the plan has no pool for them (a plan made before the personas, or a Driver with no profile).
    private const string CollectionFailedRemark = "Nobody at the collection address";
    private const string DeliveryFailedRemark = "The Consignee was closed";
    private const string DamageRemark = "A pallet was damaged";
    private const string ReservationsRemark = "One carton was dented";
    private const string FallbackReceiver = "Simulated consignee";

    public async Task<DriverResult> RunAsync(
        SimulationRun run, IReadOnlyList<RunningTrip> trips, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        if (trips.Count == 0)
        {
            return DriverResult.None;
        }

        var journeys = DriverFunnel.Plan(run.Settings.Drivers, run.Settings.Seed, trips.Sum(trip => trip.Shipments.Count));
        var first = 0;
        var started = 0;
        var completed = 0;
        var failures = new List<ExpectedFailure>();
        foreach (var trip in trips)
        {
            if (stopSending.IsCancellationRequested)
            {
                return new DriverResult(true, started, completed, failures);
            }

            var outcome = await WorkAsync(run, trip, [.. journeys.Skip(first).Take(trip.Shipments.Count)], failures, writer, stopSending, cancellationToken);
            first += trip.Shipments.Count;
            started += outcome.Started ? 1 : 0;
            completed += outcome.Completed ? 1 : 0;
            if (outcome.Stopped)
            {
                return new DriverResult(true, started, completed, failures);
            }
        }

        return new DriverResult(false, started, completed, failures);
    }

    private async Task<TripOutcome> WorkAsync(
        SimulationRun run, RunningTrip trip, IReadOnlyList<DriverJourney> journeys, List<ExpectedFailure> failures, RunLogWriter writer, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        var driver = trip.Driver;
        var reached = await WaitAsync(driver, trip.TripNumber, _ => true, stopSending, cancellationToken);
        if (reached.Stopped)
        {
            return TripOutcome.Cancelled(started: false);
        }

        if (reached.Snapshot is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), $"Trip {trip.TripNumber} didn't reach the Driver app in time: {driver.Name} doesn't start it", cancellationToken);
            return TripOutcome.NotStarted;
        }

        var start = await app.StartTripAsync(driver, trip.TripNumber, cancellationToken);
        await writer.DriverAsync(time.GetUtcNow(), driver, trip.TripNumber, null, DriverActions.StartTrip, start, null, cancellationToken);
        if (start.Outcome != LogOutcome.DriverReported)
        {
            return TripOutcome.NotStarted;
        }

        // The Driver app lets the Driver work the Stops once the TMS has recorded the start.
        var underWay = await WaitAsync(driver, trip.TripNumber, snapshot => snapshot.Status != DriverTripStatus.Dispatched, stopSending, cancellationToken);
        if (underWay.Stopped)
        {
            return TripOutcome.Cancelled(started: true);
        }

        if (underWay.Snapshot is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), $"The TMS didn't record the start of Trip {trip.TripNumber} in time: {driver.Name} doesn't work its Stops", cancellationToken);
            return TripOutcome.Underway;
        }

        for (var position = 0; position < trip.Shipments.Count; position++)
        {
            if (stopSending.IsCancellationRequested)
            {
                return TripOutcome.Cancelled(started: true);
            }

            await WorkShipmentAsync(run, trip, trip.Shipments[position], journeys[position], failures, writer, cancellationToken);
        }

        var end = await app.EndTripAsync(driver, trip.TripNumber, cancellationToken);
        await writer.DriverAsync(time.GetUtcNow(), driver, trip.TripNumber, null, DriverActions.EndTrip, end, null, cancellationToken);
        if (end.Outcome != LogOutcome.DriverReported)
        {
            return TripOutcome.Underway;
        }

        var ended = await WaitAsync(driver, trip.TripNumber, snapshot => snapshot.Status == DriverTripStatus.Completed, stopSending, cancellationToken);
        if (ended.Stopped)
        {
            return TripOutcome.Cancelled(started: true);
        }

        if (ended.Snapshot is null)
        {
            await writer.NoteAsync(time.GetUtcNow(), $"The TMS didn't record the end of Trip {trip.TripNumber} in time", cancellationToken);
            return TripOutcome.Underway;
        }

        await writer.DriverAsync(
            time.GetUtcNow(), driver, trip.TripNumber, null, DriverActions.EndTrip, new DriverCallAnswer(LogOutcome.TripCompleted, end.CorrelationId, null),
            "The TMS recorded the Trip as Completed", cancellationToken);
        return TripOutcome.Done;
    }

    private async Task WorkShipmentAsync(
        SimulationRun run, RunningTrip trip, OwnFleetShipment shipment, DriverJourney journey, List<ExpectedFailure> failures, RunLogWriter writer, CancellationToken cancellationToken)
    {
        var seed = run.Settings.Seed;
        var driver = trip.Driver;
        var number = shipment.ShipmentNumber;

        Task ReportAsync(string action, DriverCallAnswer answer, string? detail) =>
            writer.DriverAsync(time.GetUtcNow(), driver, trip.TripNumber, shipment, action, answer, detail, cancellationToken);

        if (journey.Damaged)
        {
            var said = DrawFor(run, driver, shipment, TextCategory.DriverDamageRemark);
            var damage = await app.ReportDamageAsync(driver, trip.TripNumber, number, atDelivery: false, said?.Text ?? DamageRemark, cancellationToken);
            await ReportAsync(DriverActions.ReportDamage, damage, WithReference("At the collection", said));
            Expect(failures, shipment, damage, ExceptionKinds.Damaged);
        }

        if (journey.CollectionFailed)
        {
            var said = DrawFor(run, driver, shipment, TextCategory.DriverCollectionFailedRemark);
            var failed = await app.FailCollectionAsync(driver, trip.TripNumber, number, said?.Text ?? CollectionFailedRemark, cancellationToken);
            await ReportAsync(DriverActions.CollectionFailed, failed, WithReference(null, said));
            Expect(failures, shipment, failed, ExceptionKinds.CollectionFailed);
            return;
        }

        var collected = await app.CollectAsync(driver, trip.TripNumber, number, cancellationToken);
        await ReportAsync(DriverActions.Collected, collected, null);
        if (collected.Outcome != LogOutcome.DriverReported)
        {
            return;
        }

        if (journey.DeliveryFailed)
        {
            var said = DrawFor(run, driver, shipment, TextCategory.DriverDeliveryFailedRemark);
            var failed = await app.FailDeliveryAsync(driver, trip.TripNumber, number, said?.Text ?? DeliveryFailedRemark, cancellationToken);
            await ReportAsync(DriverActions.DeliveryFailed, failed, WithReference(null, said));
            Expect(failures, shipment, failed, ExceptionKinds.DeliveryFailed);
            return;
        }

        // The Consignee's contact receives the goods; the files are drawn from the seed and the Shipment number, so they differ per delivery and repeat per run.
        var receiver = shipment.Booking.ConsigneeContact ?? shipment.Booking.ConsigneeName ?? FallbackReceiver;
        var photos = journey.Reservations ? new[] { (ReadOnlyMemory<byte>)DeliveryFiles.Photo(seed, number) } : [];
        var delivered = await app.DeliverAsync(
            driver, trip.TripNumber, number,
            new DriverDelivery(receiver, journey.Reservations, journey.Reservations ? ReservationsRemark : null, DeliveryFiles.Signature(seed, number), photos), cancellationToken);
        await ReportAsync(DriverActions.Deliver, delivered, journey.Reservations ? "With reservations" : "Clean");
        if (journey.Reservations)
        {
            Expect(failures, shipment, delivered, ExceptionKinds.Reservations);
        }
    }

    // The text the seed draws for this Shipment from its Driver's pool, or none when the plan has no such pool (a plan made before the personas).
    private static PoolDraw? DrawFor(SimulationRun run, SimulatedDriver driver, OwnFleetShipment shipment, TextCategory category) =>
        run.Plan.DriverPoolFor(driver.Id, category)?.Draw(run.Settings.Seed, shipment.Booking.Step);

    // The log keeps the pool and the index of a drawn text, never the text (coding guidelines §7).
    private static string? WithReference(string? detail, PoolDraw? drawn) =>
        drawn is null ? detail : detail is null ? drawn.Reference : $"{detail}, {drawn.Reference}";

    // The TMS opens an Exception for a report it records; the Planner looks for it later.
    private static void Expect(List<ExpectedFailure> failures, OwnFleetShipment shipment, DriverCallAnswer answer, string kind)
    {
        if (answer.Outcome == LogOutcome.DriverReported)
        {
            failures.Add(new ExpectedFailure(shipment, kind));
        }
    }

    // The estate is asked every interval, up to the attempts: the Driver app shows what the Logic Apps have done a little after it is done.
    private async Task<Waited> WaitAsync(
        SimulatedDriver driver, string tripNumber, Func<DriverTripSnapshot, bool> reached, CancellationToken stopSending, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= pacing.PollAttempts; attempt++)
        {
            if (attempt > 1 && pacing.PollInterval > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(pacing.PollInterval, time, stopSending);
                }
                catch (OperationCanceledException) when (stopSending.IsCancellationRequested)
                {
                    return new Waited(null, Stopped: true);
                }
            }

            if (stopSending.IsCancellationRequested)
            {
                return new Waited(null, Stopped: true);
            }

            var trips = await app.GetTripsAsync(driver, cancellationToken);
            if (trips?.FirstOrDefault(trip => trip.TripNumber == tripNumber) is { } found && reached(found))
            {
                return new Waited(found, Stopped: false);
            }
        }

        return new Waited(null, Stopped: false);
    }

    private sealed record Waited(DriverTripSnapshot? Snapshot, bool Stopped);

    private sealed record TripOutcome(bool Started, bool Completed, bool Stopped)
    {
        public static TripOutcome NotStarted { get; } = new(false, false, false);

        public static TripOutcome Underway { get; } = new(true, false, false);

        public static TripOutcome Done { get; } = new(true, true, false);

        public static TripOutcome Cancelled(bool started) => new(started, false, true);
    }
}
